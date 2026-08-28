namespace Rycolab.Core;

public sealed record GuardTick(
    DateTime Ts, int Elapsed, bool Ok, int?[] Hardware, int Whea, double? CpuLoad, double? PackagePower, string State);

public sealed class GuardOptions
{
    public int? Minutes { get; init; }
    public int IntervalSeconds { get; init; } = 60;
    public int MaxReappliesPerHour { get; init; } = 3;
    public int ResumeSettleSeconds { get; init; } = 10;
    public string RunsDir { get; init; } = Path.Combine(Plan.RepoRoot, "runs", "guard");
}

/// <summary>
/// Profile guardian: applies the profile, re-applies it after resuming from
/// sleep (the BIOS restores the baseline on wake, seen 2026-08-28), reads the
/// hardware back and counts WHEA events every interval, and leaves the
/// baseline in place on exit.
///
/// Exit codes: 0 ended cleanly (time up, Ctrl+C or stop file), 10 positive
/// (WHEA, or margin lost more often than allowed), 1 could not apply.
/// </summary>
public sealed class Guard
{
    private readonly CoController _co;
    private readonly Plan _plan;
    private readonly GuardOptions _o;
    private readonly Journal _journal;
    private readonly Store _store;
    private readonly Telemetry? _telemetry;
    private readonly Action<GuardTick> _onTick;
    private readonly Action<string> _onEvent;

    private readonly List<DateTime> _reapplies = [];
    private DateTime? _resumeAt;
    private volatile bool _suspendSeen;

    public Guard(CoController co, Plan plan, GuardOptions options, Telemetry? telemetry, Action<GuardTick> onTick, Action<string> onEvent)
    {
        _co = co;
        _plan = plan;
        _o = options;
        _telemetry = telemetry;
        _onTick = onTick;
        _onEvent = onEvent;
        Directory.CreateDirectory(_o.RunsDir);
        File.Delete(StopFile(_o.RunsDir));
        _journal = new Journal(Path.Combine(_o.RunsDir, "guard.jsonl"));
        _store = new Store(Path.Combine(_o.RunsDir, "rycolab.db"));
    }

    /// <summary>File that 'task stop' drops to request a clean exit (killing the process would not restore the baseline).</summary>
    public static string StopFile(string runsDir) => Path.Combine(runsDir, "stop");

    public int Run(CancellationToken ct)
    {
        var t0 = DateTime.Now;
        var code = 0;
        Event("start", $"profile {string.Join(",", _plan.Profile)}  interval {_o.IntervalSeconds}s  {(_o.Minutes is { } m ? m + " min" : "no time limit")}");

        try
        {
            if (!ApplyPlan("start")) return 1;

            using var power = new PowerWatch();
            power.Suspending += () => { _suspendSeen = true; Event("suspend", "the system is going to sleep"); };
            power.Resumed += () => { _resumeAt = DateTime.Now; Event("resume", "the system resumed; re-applying in a few seconds"); };

            var wheaSeen = 0;
            var lastLoop = DateTime.Now;
            while (!ct.IsCancellationRequested)
            {
                if (!Wait(ct)) break;

                // The resume event can arrive late or not at all (2026-08-28 10:14: the sample
                // ran 3 s before Windows logged the resume). A clock jump, or a suspend with no
                // resume, count the same.
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
                    if (!ApplyPlan("resume")) { code = 1; break; }
                }

                var readings = _co.ReadAll();
                var hw = readings.Select(x => x.Margin).ToArray();
                var bad = _plan.Mismatches(readings);
                var hardware = Whea.HardwareSince(t0);
                var el = (int)(DateTime.Now - t0).TotalSeconds;
                var cpu = _telemetry?.CpuLoad();
                var pkg = _telemetry?.Read().PackagePower;

                if (hardware.Count > wheaSeen)
                {
                    foreach (var e in hardware.Skip(wheaSeen))
                        Event("whea", $"{e.Time:HH:mm:ss} {e.Provider.Replace("Microsoft-Windows-", "")} id {e.Id}: {e.Message}");
                    wheaSeen = hardware.Count;
                    Journal.WriteJsonFile(Path.Combine(_o.RunsDir, "positives", $"whea-{DateTime.Now:yyyyMMdd-HHmmss}.json"),
                        new { plan = _plan, hardware = hw, events = hardware });
                    Tick(t0, el, bad.Count == 0, hw, hardware.Count, cpu, pkg, "WHEA");
                    code = 10;
                    break;
                }

                if (bad.Count > 0 && _resumeAt is null)
                {
                    Event("changed", $"hardware {string.Join(",", hw)}  cores off plan: {string.Join(",", bad)}");
                    _reapplies.RemoveAll(t => (DateTime.Now - t).TotalHours >= 1);
                    if (_reapplies.Count >= _o.MaxReappliesPerHour)
                    {
                        Event("giveup", $"{_reapplies.Count} re-applies within an hour; leaving the baseline");
                        Tick(t0, el, false, hw, hardware.Count, cpu, pkg, "lost");
                        code = 10;
                        break;
                    }
                    _reapplies.Add(DateTime.Now);
                    if (!ApplyPlan("lost")) { code = 1; break; }
                    Tick(t0, el, true, _co.ReadAll().Select(x => x.Margin).ToArray(), hardware.Count, cpu, pkg, "reapplied");
                    continue;
                }

                Tick(t0, el, bad.Count == 0, hw, hardware.Count, cpu, pkg, bad.Count == 0 ? "ok" : "waiting for resume");

                if (_o.Minutes is { } min && el >= min * 60) { Event("done", $"{min} min completed"); break; }
            }
            if (ct.IsCancellationRequested) Event("cancel", "interrupted with Ctrl+C");
        }
        catch (Exception ex)
        {
            Event("error", ex.Message);
            code = 1;
        }
        finally
        {
            var restored = _co.TryRestore(_plan.Base);
            var after = _co.ReadAll().Select(x => x.Margin).ToArray();
            Event("restore", $"baseline {_plan.Base}: {restored} cores written; hardware {string.Join(",", after)}  code {code}");
            _journal.Dispose();
            _store.Dispose();
        }
        return code;
    }

    /// <summary>
    /// Three attempts 5 s apart: right after waking, the SMU may reject the
    /// first write (2026-08-28 10:14, "core 12: the SMU rejected the write").
    /// </summary>
    private bool ApplyPlan(string why, int attempts = 3)
    {
        for (var i = 1; i <= attempts; i++)
        {
            try
            {
                var after = Stepper.Apply(_co, _plan.Targets(_co.CoreCount));
                Event("apply", $"{why}: profile applied and verified{(i > 1 ? $" on attempt {i}" : "")}: {string.Join(",", after.Select(x => x.Margin))}");
                return true;
            }
            catch (Exception ex)
            {
                Event("apply-failed", $"{why}: attempt {i}/{attempts}: {ex.Message}");
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
                Event("stop", "stop requested with 'task stop'");
                return false;
            }
            if (_resumeAt is not null) return true;   // do not wait the whole interval after waking
            Thread.Sleep(250);
        }
        return true;
    }

    private void Tick(DateTime t0, int el, bool ok, int?[] hw, int whea, double? cpu, double? pkg, string state)
    {
        var t = new GuardTick(DateTime.Now, el, ok, hw, whea, cpu, pkg, state);
        _journal.Write(new { kind = "tick", t.Ts, t.Elapsed, t.Ok, t.Hardware, t.Whea, t.CpuLoad, t.PackagePower, t.State });
        _store.AddTick(t);
        _onTick(t);
    }

    private void Event(string kind, string detail)
    {
        _journal.Write(new { kind, ts = DateTime.Now, detail });
        _store.AddEvent(DateTime.Now, kind, detail);
        _onEvent($"{DateTime.Now:HH:mm:ss}  {kind}: {detail}");
    }
}
