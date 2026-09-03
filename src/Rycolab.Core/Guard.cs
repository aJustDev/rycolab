using Rycolab.Core.Legion;

namespace Rycolab.Core;

public sealed record GuardTick(
    DateTime Ts, int Elapsed, bool Ok, int?[] Hardware, int Whea, double? CpuLoad, double? PackagePower, string State, TickExtras? Extras = null);

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
    private readonly Store _store;
    private long _session;
    private LenovoEc? _ec;
    private LenovoEnergy? _energy;
    private int? _smuMs;
    private readonly HashSet<string> _failedSources = [];
    private readonly CpuLoad _load = new();
    private readonly PmTable? _pm;
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

    // Bad news becomes a toast. A flapping margin fires "changed" every
    // interval; the per-kind cooldown keeps that to one toast per 10 min.
    private static readonly Dictionary<string, string> BadNews = new()
    {
        ["whea"] = "rycolab: WHEA event",
        ["reset"] = "rycolab: machine reset detected",
        ["changed"] = "rycolab: margin lost",
        ["giveup"] = "rycolab: guard gave up",
        ["apply-failed"] = "rycolab: profile apply failed",
        ["error"] = "rycolab: guard error",
        ["dgpu-stuck"] = "rycolab: dGPU stuck awake",
    };
    private readonly Dictionary<string, DateTime> _notified = [];
    private const int NotifyCooldownMinutes = 10;

    private DateTime? _lastHealthAt;

    public Guard(CoController co, Profile profile, GuardOptions options, Action<GuardTick> onTick, Action<string> onEvent, PmTable? pm = null)
    {
        _co = co;
        _profile = profile;
        _o = options;
        _pm = pm;
        _onTick = onTick;
        _onEvent = onEvent;
        Directory.CreateDirectory(_o.RunsDir);
        File.Delete(StopFile(_o.RunsDir));
        _store = Store.Open();
        if (_o.PublishState) _validation = Validation.LoadFor(profile);
        _lastHealthAt = _store.LastHealthTs();
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
        _session = _store.BeginSession(Environment.ProcessId, string.Join(",", _profile.Cores), _o.IntervalSeconds, adhoc: !_o.PublishState);
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
            if (!ApplyProfile("start")) { code = 1; return 1; }
            // The EC and the Energy driver are read every tick (temperatures, fans, modes, charge mode); one handle each for the whole session.
            try { _ec = new LenovoEc(); } catch { _ec = null; }
            try { _energy = new LenovoEnergy(); } catch { _energy = null; }
            // A sample right away: `on` waits for a tick, and the first interval is a minute.
            Tick(0, true, ReadTimed().Select(x => x.Margin).ToArray(), 0, null, PackagePower(), "ok");

            using var power = new PowerWatch();
            power.Suspending += () => { _suspendSeen = true; Event("suspend", "the system is going to sleep"); };
            power.Resumed += () => { _resumeAt = DateTime.Now; Event("resume", "the system resumed; re-applying in a few seconds"); };
            power.AcLineChanged += ac => { lock (_acLock) { _acPending = ac; _acSince = DateTime.Now; } };
            if (_o.PublishState && Plan.LoadOrDefault().PowerAuto && BatteryInfo.OnAcLine() is { } line)
                lock (_acLock) { _acPending = line; _acSince = DateTime.Now; }

            var wheaSeen = 0;
            var ignoredSeen = 0;
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

                Safe("charge-full", ChargeFullTick);
                Safe("health", HealthTick);
                Safe("dgpu-eject", DgpuEjectTick);

                var readings = ReadTimed();
                var hw = readings.Select(x => x.Margin).ToArray();
                var bad = _profile.Mismatches(readings);
                var hardware = Whea.HardwareSince(_t0);
                var ignored = Whea.IgnoredSince(_t0);
                for (; ignoredSeen < ignored.Count; ignoredSeen++)
                    Event("whea-info", $"{ignored[ignoredSeen].Time:HH:mm:ss} WHEA id {ignored[ignoredSeen].Id} not counted (PCIe, not a core): {ignored[ignoredSeen].Message}");
                var el = (int)(DateTime.Now - _t0).TotalSeconds;
                var cpu = _load.Percent();
                var pkg = PackagePower();

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
                    _state.Positive = "whea";
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
                        _state.Positive = "lost";
                        code = 10;
                        break;
                    }
                    _reapplies.Add(DateTime.Now);
                    if (_validation is not null) _validation.Reapplies++;
                    if (!ApplyProfile("lost")) { code = 1; break; }
                    Tick(el, true, ReadTimed().Select(x => x.Margin).ToArray(), hardware.Count, cpu, pkg, "reapplied");
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
            var after = ReadTimed().Select(x => x.Margin).ToArray();
            _state.Hardware = after;
            _state.Applied = false;
            _state.GuardPid = null;
            _state.Phase = code == 10 ? "positive" : "off";
            Event("restore", $"baseline {_profile.Base}: {restored} cores written; hardware {string.Join(",", after)}  code {code}");
            _store.EndSession(_session, code);
            _ec?.Dispose();
            _energy?.Dispose();
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
            Safe("power-auto", PowerAutoTick);
            Thread.Sleep(250);
        }
        return true;
    }

    /// <summary>
    /// The battery/EC conveniences run in the guard's loop but are not what
    /// it guards: a WMI or driver hiccup in one of them is an event, never
    /// an exit that would leave the undervolt unwatched.
    /// </summary>
    private void Safe(string what, Action tick)
    {
        try { tick(); }
        catch (Exception ex) { Event("tick-failed", $"{what}: {ex.Message}"); }
    }

    /// <summary>Applies the battery/AC profile once the line has been stable for the debounce, if `legion power auto` is on.</summary>
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

    /// <summary>Ends a `legion charge full`: once the battery hits the target, back to the previous mode.</summary>
    private void ChargeFullTick()
    {
        if (!_o.PublishState || ChargeFull.Load() is not { } full) return;
        if (BatteryInfo.Read().Percent is not { } p || p < full.Target) return;
        using var energy = new LenovoEnergy();
        var after = energy.IsAvailable ? energy.SetChargeMode(full.Restore) : null;
        ChargeFull.Delete();
        Event("charge", $"battery at {p:F0} % -> full charge done, mode back to {after ?? "?"}{(after == full.Restore ? "" : " (NOT CONFIRMED)")}");
    }

    /// <summary>
    /// Finishes a pending dGPU ejection: nudges the EC every tick (the notify
    /// makes it retry), and after 6 min disables the node as a last resort
    /// worth ~12 W and says so with a toast - burning ~25 W of battery in
    /// silence is not acceptable. Never needed since the probe fix; kept as
    /// the safety net.
    /// </summary>
    private void DgpuEjectTick()
    {
        if (!_o.PublishState || DgpuEject.Load() is not { } eject) return;
        if (!LenovoEc.DgpuPresent())
        {
            DgpuEject.Delete();
            Event("power", $"dGPU ejected {(int)(DateTime.Now - eject.Started).TotalSeconds} s after the switch");
            return;
        }
        using var ec = new LenovoEc();
        if (!ec.IsAvailable || ec.IGpuMode != LenovoEc.IGpuOnly) { DgpuEject.Delete(); return; }
        if ((DateTime.Now - eject.Started).TotalMinutes < 6) { ec.NotifyDgpuStatus(true); return; }
        DgpuEject.Delete();
        var lines = new List<string>();
        PowerProfile.Dgpu("disable-device", lines.Add);
        Event("dgpu-stuck", $"dGPU still on the bus 6 min after the switch; {string.Join(" | ", lines)}; the silicon keeps ~20 W without a driver, a reboot truly powers it off");
    }

    /// <summary>One battery-health sample per day.</summary>
    private void HealthTick()
    {
        if (!_o.PublishState || _lastHealthAt?.Date == DateTime.Now.Date) return;
        var s = BatteryHealth.Read();
        if (s.FullWh is null) { _lastHealthAt = s.Ts; return; }   // no battery here; do not retry every minute
        _store.AddHealth(s);
        _lastHealthAt = s.Ts;
    }

    /// <summary>
    /// The SMU's own package float from the PM table; null on a table version
    /// without a calibrated index (the guard does not need it, the panel only
    /// shows it). No LibreHardwareMonitor here: its driver and its hang on
    /// Dispose have no place in the process that runs for days.
    /// </summary>
    private double? PackagePower()
        => _pm?.Refresh() == true ? _pm.Package() : null;

    /// <summary>The margins of every core, timed: the SMU mailbox has no cross-process lock, and contention shows here before it shows as a wrong readback.</summary>
    private IReadOnlyList<CoreReading> ReadTimed()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = _co.ReadAll();
        _smuMs = (int)sw.ElapsedMilliseconds;
        return r;
    }

    /// <summary>
    /// Everything else the tick records: battery, Lenovo EC, power and GPU
    /// mode, panel. A source that fails leaves its columns null and is
    /// reported once, not every minute.
    /// </summary>
    private TickExtras Extras()
    {
        bool? ac = null; double? batW = null, batPct = null, batWh = null, batFull = null, chargeW = null;
        int? ecCpu = null, ecGpu = null, ecPch = null, fanCpu = null, fanGpu = null, fanPch = null, mode = null, gpu = null, hz = null, bright = null;
        double? coreTempMax = null, coreVoltMean = null, coreGhzMax = null; int? coreHot = null, idle = null; string? chargeMode = null, overlay = null; bool? dgpu = null;
        Source("battery", () => { var b = BatteryInfo.Read(); ac = b.OnAc; batW = b.DischargeW; batPct = b.Percent; batWh = b.RemainingWh; batFull = b.FullWh; chargeW = b.ChargeW; });
        // The PM table was refreshed for the package power a moment ago (PackagePower); the per-core blocks come from the same read.
        if (_pm is { IsAvailable: true } pm)
            Source("pm", () =>
            {
                var cores = Enumerable.Range(0, _co.CoreCount).Select(pm.Core).ToList();
                var temps = cores.Select(c => c.Temp).OfType<double>().ToList();
                var volts = cores.Select(c => c.Volt).OfType<double>().ToList();
                var freqs = cores.Select(c => c.Freq).OfType<double>().ToList();
                if (temps.Count > 0) { coreTempMax = temps.Max(); coreHot = cores.FindIndex(c => c.Temp == coreTempMax); }
                if (volts.Count > 0) coreVoltMean = volts.Average();
                if (freqs.Count > 0) coreGhzMax = freqs.Max();
            });
        if (_energy is { IsAvailable: true } energy) Source("charge", () => chargeMode = energy.ChargeMode());
        Source("dgpu", () => dgpu = LenovoEc.DgpuPresent());
        Source("overlay", () => { var o = WindowsPower.Overlays(); overlay = ac == false ? o.Dc : o.Ac; });
        Source("idle", () => idle = UserIdle.Seconds());
        if (_ec is { IsAvailable: true } ec)
            Source("ec", () =>
            {
                // The EC reports 0 C for a dGPU that is off the bus (iGPU-only, or ejected): not a temperature.
                ecCpu = ec.CpuTempC; ecGpu = ec.GpuTempC is > 0 and var g ? g : null; ecPch = ec.PchTempC;
                fanCpu = ec.CpuFanRpm; fanGpu = ec.GpuFanRpm; fanPch = ec.PchFanRpm;
                mode = ec.SmartFanMode; gpu = ec.IGpuMode;
            });
        Source("panel", () => { hz = WindowsPower.RefreshHz; bright = WindowsPower.Brightness; });
        return new TickExtras(ac, batW, batPct, batWh, batFull, ecCpu, ecGpu, ecPch, fanCpu, fanGpu, fanPch, mode, gpu, hz, bright,
            coreTempMax, coreHot, coreVoltMean, coreGhzMax, idle, chargeW, chargeMode, dgpu, overlay, _smuMs);

        void Source(string name, Action read)
        {
            try { read(); }
            catch (Exception ex) { if (_failedSources.Add(name)) Event("tick-failed", $"{name}: {ex.Message} (its columns stay empty; not repeated)"); }
        }
    }

    private void Tick(int el, bool ok, int?[] hw, int whea, double? cpu, double? pkg, string state)
    {
        var t = new GuardTick(DateTime.Now, el, ok, hw, whea, cpu, pkg, state, Extras());
        _store.AddTick(_session, t);

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
        _store.AddEvent("guard", _session, null, DateTime.Now, kind, detail);
        _state.LastEvents.Add($"{DateTime.Now:HH:mm:ss}  {kind}: {detail}");
        if (_state.LastEvents.Count > 10) _state.LastEvents.RemoveAt(0);
        PublishState();
        _onEvent($"{DateTime.Now:HH:mm:ss}  {kind}: {detail}");

        if (_o.PublishState && BadNews.TryGetValue(kind, out var title)
            && (!_notified.TryGetValue(kind, out var last) || (DateTime.Now - last).TotalMinutes >= NotifyCooldownMinutes)
            && Plan.LoadOrDefault().Notify)
        {
            _notified[kind] = DateTime.Now;
            Notifier.Notify(title, detail);
        }
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
