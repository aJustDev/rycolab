namespace Rycolab.Core;

public sealed record GuardTick(
    DateTime Ts, int Elapsed, bool Ok, int?[] Hardware, int Whea, double? CpuLoad, double? PackagePower, string State);

public sealed class GuardOptions
{
    public int? Minutes { get; init; }
    public int IntervalSeconds { get; init; } = 60;
    public int MaxReappliesPerHour { get; init; } = 3;
    public int ResumeSettleSeconds { get; init; } = 10;
    public string RunsDir { get; init; } = AppPaths.Guard;
    /// <summary>Write state.json / validation.json (the installed profile) or not (ad-hoc soaks).</summary>
    public bool PublishState { get; init; } = true;
}

/// <summary>
/// Profile guardian: applies the profile, re-applies it after resuming from
/// sleep (the BIOS restores the baseline on wake), reads the hardware back
/// and counts WHEA events every interval, and leaves the baseline in place
/// on exit. Publishes state.json for the unelevated `status`.
///
/// Exit codes: 0 ended cleanly (time up, Ctrl+C or stop file), 10 positive
/// (WHEA, or margin lost more often than allowed), 1 could not apply.
/// </summary>
public sealed class Guard
{
    private readonly CoController _co;
    private readonly Profile _profile;
    private readonly GuardOptions _o;
    private readonly Journal _journal;
    private readonly Store _store;
    private readonly Telemetry? _telemetry;
    private readonly Action<GuardTick> _onTick;
    private readonly Action<string> _onEvent;

    private readonly List<DateTime> _reapplies = [];
    private DateTime? _resumeAt;
    private volatile bool _suspendSeen;

    // Power auto: the AC line state waiting to be acted on, and since when. Spontaneous
    // line blips of a few seconds were logged on this machine; the debounce ignores them.
    private readonly object _acLock = new();
    private bool? _acPending;
    private DateTime _acSince;
    private bool? _acApplied;
    public const int AcDebounceSeconds = 15;

    private readonly State _state = new();
    private readonly Validation? _validation;
    private DateTime _t0;

    public Guard(CoController co, Profile profile, GuardOptions options, Telemetry? telemetry, Action<GuardTick> onTick, Action<string> onEvent)
    {
        _co = co;
        _profile = profile;
        _o = options;
        _telemetry = telemetry;
        _onTick = onTick;
        _onEvent = onEvent;
        Directory.CreateDirectory(_o.RunsDir);
        File.Delete(StopFile(_o.RunsDir));
        _journal = new Journal(Path.Combine(_o.RunsDir, "guard.jsonl"));
        _store = new Store(Path.Combine(_o.RunsDir, "rycolab.db"));
        if (_o.PublishState) _validation = Validation.LoadFor(profile);
    }

    /// <summary>File that `off` / `task stop` drop to request a clean exit (killing the process would not restore the baseline).</summary>
    public static string StopFile(string runsDir) => Path.Combine(runsDir, "stop");

    public int Run(CancellationToken ct)
    {
        _t0 = DateTime.Now;
        var code = 0;
        _state.GuardPid = Environment.ProcessId;
        _state.Since = _t0;
        _state.Profile = _profile.Cores;
        Event("start", $"profile {string.Join(",", _profile.Cores)}  interval {_o.IntervalSeconds}s  {(_o.Minutes is { } m ? m + " min" : "no time limit")}");

        // A hard reset leaves no WHEA and no journal line; the only trace is Kernel-Power 41 at the next boot.
        if (_validation?.LastTickAt is { } lastTick)
        {
            var resets = Whea.UnexpectedRebootsSince(lastTick);
            foreach (var r in resets) Event("reset", $"unexpected reboot recorded at {r.Time:yyyy-MM-dd HH:mm:ss}; last guard tick {lastTick:HH:mm:ss}, no WHEA");
            _validation.Resets += resets.Count;
        }

        try
        {
            if (!ApplyProfile("start")) return 1;

            using var power = new PowerWatch();
            power.Suspending += () => { _suspendSeen = true; Event("suspend", "the system is going to sleep"); };
            power.Resumed += () => { _resumeAt = DateTime.Now; Event("resume", "the system resumed; re-applying in a few seconds"); };
            power.AcLineChanged += ac => { lock (_acLock) { _acPending = ac; _acSince = DateTime.Now; } };
            if (_o.PublishState && Plan.LoadOrDefault().PowerAuto && BatteryInfo.OnAcLine() is { } line)
                lock (_acLock) { _acPending = line; _acSince = DateTime.Now; }

            var wheaSeen = 0;
            var lastLoop = DateTime.Now;
            while (!ct.IsCancellationRequested)
            {
                if (!Wait(ct)) break;

                // The resume event can arrive late or not at all (the sample can run before
                // Windows logs the resume). A clock jump, or a suspend with no resume, count the same.
                var gap = (DateTime.Now - lastLoop).TotalSeconds;
                lastLoop = DateTime.Now;
                if (_resumeAt is null && (_suspendSeen || gap > 2 * _o.IntervalSeconds + 30))
                {
                    _resumeAt = DateTime.Now;
                    Event("resume", _suspendSeen ? "resume inferred from the earlier suspend" : $"resume inferred from a {gap:F0} s clock jump");
                }
                _suspendSeen = false;

                if (_resumeAt is { } r)
                {
                    var settle = _o.ResumeSettleSeconds - (DateTime.Now - r).TotalSeconds;
                    if (settle > 0) Thread.Sleep(TimeSpan.FromSeconds(settle));
                    _resumeAt = null;
                    if (_validation is not null) _validation.Resumes++;
                    if (!ApplyProfile("resume")) { code = 1; break; }
                }

                var readings = _co.ReadAll();
                var hw = readings.Select(x => x.Margin).ToArray();
                var bad = _profile.Mismatches(readings);
                var hardware = Whea.HardwareSince(_t0);
                var el = (int)(DateTime.Now - _t0).TotalSeconds;
                var cpu = _telemetry?.CpuLoad();
                var pkg = _telemetry?.Read().PackagePower;

                if (hardware.Count > wheaSeen)
                {
                    var fresh = hardware.Count - wheaSeen;
                    foreach (var e in hardware.Skip(wheaSeen))
                        Event("whea", $"{e.Time:HH:mm:ss} {e.Provider.Replace("Microsoft-Windows-", "")} id {e.Id}: {e.Message}");
                    wheaSeen = hardware.Count;
                    Journal.WriteJsonFile(Path.Combine(_o.RunsDir, "positives", $"whea-{DateTime.Now:yyyyMMdd-HHmmss}.json"),
                        new { profile = _profile, hardware = hw, events = hardware });
                    if (_validation is not null) _validation.Whea += fresh;
                    Tick(el, bad.Count == 0, hw, hardware.Count, cpu, pkg, "WHEA");
                    code = 10;
                    break;
                }

                if (bad.Count > 0 && _resumeAt is null)
                {
                    Event("changed", $"hardware {string.Join(",", hw)}  cores off profile: {string.Join(",", bad)}");
                    _reapplies.RemoveAll(t => (DateTime.Now - t).TotalHours >= 1);
                    if (_reapplies.Count >= _o.MaxReappliesPerHour)
                    {
                        Event("giveup", $"{_reapplies.Count} re-applies within an hour; leaving the baseline");
                        Tick(el, false, hw, hardware.Count, cpu, pkg, "lost");
                        code = 10;
                        break;
                    }
                    _reapplies.Add(DateTime.Now);
                    if (_validation is not null) _validation.Reapplies++;
                    if (!ApplyProfile("lost")) { code = 1; break; }
                    Tick(el, true, _co.ReadAll().Select(x => x.Margin).ToArray(), hardware.Count, cpu, pkg, "reapplied");
                    continue;
                }

                if (bad.Count == 0 && _validation is not null) _validation.GuardedSeconds += _o.IntervalSeconds;
                Tick(el, bad.Count == 0, hw, hardware.Count, cpu, pkg, bad.Count == 0 ? "ok" : "waiting for resume");

                if (_o.Minutes is { } min && el >= min * 60) { Event("done", $"{min} min completed"); break; }
            }
            if (ct.IsCancellationRequested) Event("cancel", "interrupted with Ctrl+C");
        }
        catch (Exception ex)
        {
            Event("error", ex.Message);
            _state.LastError = ex.Message;
            code = 1;
        }
        finally
        {
            var restored = _co.TryRestore(_profile.Base);
            var after = _co.ReadAll().Select(x => x.Margin).ToArray();
            _state.Hardware = after;
            _state.Applied = false;
            _state.GuardPid = null;
            _state.Phase = code == 10 ? "positive" : "off";
            Event("restore", $"baseline {_profile.Base}: {restored} cores written; hardware {string.Join(",", after)}  code {code}");
            _journal.Dispose();
            _store.Dispose();
        }
        return code;
    }

    /// <summary>
    /// Three attempts 5 s apart: right after waking, the SMU may reject the
    /// first write.
    /// </summary>
    private bool ApplyProfile(string why, int attempts = 3)
    {
        for (var i = 1; i <= attempts; i++)
        {
            try
            {
                var after = Stepper.Apply(_co, _profile.Targets(_co.CoreCount));
                _state.Hardware = after.Select(x => x.Margin).ToArray();
                _state.Applied = true;
                Event("apply", $"{why}: profile applied and verified{(i > 1 ? $" on attempt {i}" : "")}: {string.Join(",", after.Select(x => x.Margin))}");
                return true;
            }
            catch (Exception ex)
            {
                Event("apply-failed", $"{why}: attempt {i}/{attempts}: {ex.Message}");
                _state.LastError = ex.Message;
                if (i < attempts) Thread.Sleep(5000);
            }
        }
        return false;
    }

    private bool Wait(CancellationToken ct)
    {
        var deadline = DateTime.Now.AddSeconds(_o.IntervalSeconds);
        while (DateTime.Now < deadline)
        {
            if (ct.IsCancellationRequested) return false;
            if (File.Exists(StopFile(_o.RunsDir)))
            {
                File.Delete(StopFile(_o.RunsDir));
                Event("stop", "stop requested with 'rycolab off'");
                return false;
            }
            if (_resumeAt is not null) return true;   // do not wait the whole interval after waking
            PowerAutoTick();
            Thread.Sleep(250);
        }
        return true;
    }

    /// <summary>Applies the battery/AC profile once the line has been stable for the debounce, if `power auto` is on.</summary>
    private void PowerAutoTick()
    {
        bool target;
        lock (_acLock)
        {
            if (_acPending is not { } p || (DateTime.Now - _acSince).TotalSeconds < AcDebounceSeconds) return;
            _acPending = null;
            target = p;
        }
        if (!_o.PublishState) return;
        var plan = Plan.LoadOrDefault();
        if (!plan.PowerAuto) { _state.PowerProfile = null; return; }
        if (target == _acApplied) return;
        if (BatteryInfo.OnAcLine() is { } now && now != target) return;   // changed again meanwhile; a new event is pending

        using var ec = new LenovoEc();
        if (!ec.IsAvailable) { Event("power", "power auto is on but there is no Lenovo EC here"); return; }
        var lines = new List<string>();
        var failed = target ? PowerProfile.Ac(ec, lines.Add) : PowerProfile.Battery(ec, plan.PowerAutoOptions, lines.Add);
        _acApplied = target;
        _state.PowerProfile = target ? "ac" : "battery";
        Event("power", $"AC line {(target ? "back" : "off")} -> {(target ? "restored the snapshot" : $"battery profile ({plan.PowerAutoOptions})")}{(failed > 0 ? $", {failed} knob(s) FAILED" : "")}: {string.Join(" | ", lines)}");
    }

    private void Tick(int el, bool ok, int?[] hw, int whea, double? cpu, double? pkg, string state)
    {
        var t = new GuardTick(DateTime.Now, el, ok, hw, whea, cpu, pkg, state);
        _journal.Write(new { kind = "tick", t.Ts, t.Elapsed, t.Ok, t.Hardware, t.Whea, t.CpuLoad, t.PackagePower, t.State });
        _store.AddTick(t);

        _state.Hardware = hw;
        _state.Applied = ok;
        _state.LastTick = t.Ts;
        _state.LastState = state;
        _state.Whea = whea;
        _state.CpuLoad = cpu;
        _state.PackagePower = pkg;
        if (_validation is not null) _validation.LastTickAt = t.Ts;
        PublishState();
        _onTick(t);
    }

    private void Event(string kind, string detail)
    {
        _journal.Write(new { kind, ts = DateTime.Now, detail });
        _store.AddEvent(DateTime.Now, kind, detail);
        _state.LastEvents.Add($"{DateTime.Now:HH:mm:ss}  {kind}: {detail}");
        if (_state.LastEvents.Count > 10) _state.LastEvents.RemoveAt(0);
        PublishState();
        _onEvent($"{DateTime.Now:HH:mm:ss}  {kind}: {detail}");
    }

    private void PublishState()
    {
        if (!_o.PublishState) return;
        if (_validation is not null)
        {
            _validation.Save();
            _state.GuardedSeconds = _validation.GuardedSeconds;
            _state.Resumes = _validation.Resumes;
            _state.Reapplies = _validation.Reapplies;
            _state.Resets = _validation.Resets;
            _state.ValidationStartedAt = _validation.StartedAt;
            if (_state.GuardPid is not null) _state.Phase = _validation.IsSteady ? "steady" : "validating";
        }
        try { _state.Save(); } catch { /* state is a convenience; the journal is the record */ }
    }
}
