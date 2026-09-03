using Rycolab.Core.Engines;

namespace Rycolab.Core;

/// <summary><paramref name="Stage"/>: sweep (coarse search), fine (the step below the first clean coarse margin), confirm (the long run at the limit), soak (light load at limit + margin).</summary>
public sealed record RunResult(
    int Core, int Margin, string Engine, string Verdict, int Seconds, string? Error, int? ExitCode,
    int Whea, int Lines, int Suspensions, SampleSummary? Telemetry, DateTime Started, DateTime Ended, string Stage = "sweep");

public sealed class SweepOptions
{
    public int[] Cores { get; init; } = Enumerable.Range(0, Topology.MaxCores).ToArray();
    public int? Start { get; init; }
    public int? Top { get; init; }
    public int? Step { get; init; }
    public int? Seconds { get; init; }
    public bool Suspend { get; init; } = true;
    /// <summary>Where the campaign keeps its files (positives, work); its name is the directory name.</summary>
    public required string CampaignDir { get; init; }
    /// <summary>`find --quick`: recorded on the campaign so its limits are not mistaken for a real one.</summary>
    public bool Quick { get; init; }
}

public interface ISweepSink
{
    void RunStarted(int core, int margin, string engine, string stage, int seconds);
    void Progress(Sample sample, EngineStatus status);
    void RunEnded(RunResult result);
    void CoreDone(int core, int? limit);
    void Event(string line);
}

/// <summary>
/// Per-core limit search in four stages, every run at a verified margin and
/// every run restoring the baseline:
///
///   sweep    from the start margin upwards in coarse steps (10) with the
///            sweep engine(s); the first clean margin ends it
///   fine     the step below that margin (5), when the coarse step skipped it
///   confirm  a long run at the limit; a positive moves the limit one step up
///            and confirms again
///   soak     light load (04-P4P, the engine that reaches fMax) at
///            limit + safety margin, where the profile will actually run; a
///            positive moves the limit one step up and soaks again
///
/// The limit recorded for the core is the one that survived confirm and
/// soak. Any positive (error, crash, WHEA, hang) counts. Everything goes to
/// the database as it happens (a run row first, then a sample per second,
/// then the verdict). Resumable: cores with a limit are skipped, and a run
/// still `running` at startup betrays a machine hang or a killed process.
/// </summary>
public sealed class Sweep
{
    private readonly CoController _co;
    private readonly Plan _plan;
    private readonly SweepOptions _o;
    private readonly Telemetry? _tel;
    private readonly PmTable? _pm;
    private readonly ISweepSink _sink;
    private readonly Store _store;
    private readonly long _campaignId;

    public Dictionary<int, int?> Limits { get; }

    public string CampaignName => Path.GetFileName(_o.CampaignDir.TrimEnd('\\', '/'));

    public Sweep(CoController co, Plan plan, SweepOptions options, Telemetry? telemetry, PmTable? pm, ISweepSink sink)
    {
        _co = co; _plan = plan; _o = options; _tel = telemetry; _pm = pm; _sink = sink;
        Directory.CreateDirectory(_o.CampaignDir);
        _store = Store.Open();
        _campaignId = _store.OpenCampaign(CampaignName, _o.CampaignDir, new
        {
            plan = _plan, start = Start, top = Top, step = Step, coarse = Coarse, seconds = Seconds,
            confirmSeconds = _plan.ConfirmSeconds, soakSeconds = _plan.SoakSeconds, soakEngine = _plan.SoakEngine, suspend = _o.Suspend,
        }, _o.Cores, _o.Quick);
        Limits = _store.Limits(_campaignId);
    }

    private int Start => _o.Start ?? _plan.Start;
    private int Top => _o.Top ?? _plan.Top;
    private int Step => _o.Step ?? _plan.Step;
    private int Coarse => _plan.CoarseStep > 0 ? Math.Max(Step, _plan.CoarseStep) : Step;
    private int Seconds => _o.Seconds ?? _plan.Seconds;

    // ---- the search plan, without hardware (tested) ----

    /// <summary>The coarse margins, from the start up to the top.</summary>
    public static IEnumerable<int> CoarseMargins(int from, int top, int coarse)
    {
        for (var m = from; m <= top; m += coarse) yield return m;
    }

    /// <summary>The margins between the first clean coarse margin and the last positive one, one step below at a time. Empty when the coarse step is the step.</summary>
    public static IEnumerable<int> FineMargins(int firstClean, int lastPositive, int step)
    {
        for (var m = firstClean - step; m > lastPositive; m -= step) yield return m;
    }

    public int Run(CancellationToken ct)
    {
        var t0 = DateTime.Now;
        Note($"sweep: cores {string.Join(",", _o.Cores)}  {Start} -> {Top} coarse {Coarse} fine {Step}  {Seconds} s  engines {string.Join(" | ", _plan.Engines)}  tests {string.Join(",", _plan.Tests)}" +
                    $"  confirm {_plan.ConfirmSeconds} s  soak {_plan.SoakSeconds} s with {_plan.SoakEngine} at limit + {_plan.SafetyMargin}");

        // A y-cruncher load does not count as user activity: without this the
        // machine slept mid-run on 2026-08-31 (AC standby 1 h) and the run
        // closed as a false CLEAN at the baseline.
        KeepAwake.On();

        // A run left in progress: a machine hang (the BIOS restored the baseline by itself) if
        // the machine has rebooted since; if it is still the same boot session, the process was
        // killed (console closed, session ended) and the run simply repeats (2026-08-31 15:45).
        var hang = _store.RunningRun(_campaignId);
        RunningRun? killed = null;
        if (hang is not null)
        {
            if (SameBoot(hang.Boot, BootTime()))
            {
                killed = hang;
                hang = null;
                Note($"RUN IN PROGRESS AT STARTUP: core {killed.Core} margin {killed.Margin} {killed.Engine} since {killed.Started:HH:mm:ss}, same boot session -> the process was killed, not a hang; the run repeats", "killed");
                Record(killed.Id, new RunResult(killed.Core, killed.Margin, killed.Engine, "aborted", 0, "process killed (same boot session)", null, 0, 0, 0, null, killed.Started, DateTime.Now, killed.Stage));
            }
            else
            {
                Note($"RUN IN PROGRESS AT STARTUP: core {hang.Core} margin {hang.Margin} {hang.Engine} ({hang.Stage}) since {hang.Started:HH:mm:ss} -> machine hang, positive", "hang");
                Record(hang.Id, new RunResult(hang.Core, hang.Margin, hang.Engine, "hang", 0, "machine hang (reboot)", null, 0, 0, 0, null, hang.Started, DateTime.Now, hang.Stage));
            }
        }

        try
        {
            foreach (var core in _o.Cores)
            {
                if (ct.IsCancellationRequested) break;
                if (Limits.TryGetValue(core, out var done) && done is not null)
                {
                    Note($"core {core}: already has limit {done}, skipped");
                    continue;
                }

                // Every margin below the one in progress was a positive (a clean one would have closed the core).
                var from = Start;
                if (hang is not null && hang.Core == core) from = Math.Min(Top, hang.Margin + Step);
                if (killed is not null && killed.Core == core) from = killed.Margin;

                var limit = Search(core, from, ct);
                if (ct.IsCancellationRequested) break;
                if (limit is { } l1) limit = Confirm(core, l1, ct);
                if (ct.IsCancellationRequested) break;
                if (limit is { } l2) limit = Soak(core, l2, ct);
                if (ct.IsCancellationRequested) break;

                Limits[core] = limit;
                _store.SetLimit(_campaignId, core, limit);
                _sink.CoreDone(core, limit);
            }
        }
        finally
        {
            KeepAwake.Off();
            _co.TryRestore(_plan.Base);
        }

        if (ct.IsCancellationRequested)
            Note($"interrupted after {(DateTime.Now - t0).TotalMinutes:F0} min; baseline {_plan.Base} restored", "interrupted");
        else
        {
            Note($"sweep finished in {(DateTime.Now - t0).TotalMinutes:F0} min", "done");
            _store.EndCampaign(_campaignId);
        }
        _store.Dispose();
        return ct.IsCancellationRequested ? 1 : 0;
    }

    /// <summary>Coarse steps up to the first clean margin, then the fine steps below it. Null when nothing up to the top is clean.</summary>
    private int? Search(int core, int from, CancellationToken ct)
    {
        int? first = null;
        var lastPositive = int.MinValue;
        foreach (var m in CoarseMargins(from, Top, Coarse))
        {
            if (ct.IsCancellationRequested) return null;
            if (Clean(core, m, _plan.Engines, Seconds, "sweep", ct)) { first = m; break; }
            lastPositive = m;
        }
        if (first is not { } limit) return null;

        foreach (var m in FineMargins(limit, Math.Max(lastPositive, Start - Step), Step))
        {
            if (ct.IsCancellationRequested) return null;
            if (!Clean(core, m, _plan.Engines, Seconds, "fine", ct)) break;
            limit = m;
        }
        return limit;
    }

    /// <summary>The long run at the limit. A positive moves the limit one step up and confirms again; past the top, no limit.</summary>
    private int? Confirm(int core, int limit, CancellationToken ct)
    {
        if (_plan.ConfirmSeconds <= 0) return limit;
        while (!ct.IsCancellationRequested)
        {
            if (limit > Top) { Note($"core {core}: no margin up to the top survived confirmation"); return null; }
            if (Clean(core, limit, _plan.Engines, _plan.ConfirmSeconds, "confirm", ct)) return limit;
            Note($"core {core}: {limit} failed the {_plan.ConfirmSeconds} s confirmation; limit moves to {limit + Step}");
            limit += Step;
        }
        return null;
    }

    /// <summary>Light load where the profile will run (limit + safety margin, capped at the top). A positive moves the limit one step up and soaks again.</summary>
    private int? Soak(int core, int limit, CancellationToken ct)
    {
        if (_plan.SoakSeconds <= 0 || string.IsNullOrEmpty(_plan.SoakEngine)) return limit;
        while (!ct.IsCancellationRequested)
        {
            if (limit > Top) { Note($"core {core}: no margin up to the top survived the soak"); return null; }
            var at = Math.Min(Top, limit + _plan.SafetyMargin);
            if (Clean(core, at, [_plan.SoakEngine], _plan.SoakSeconds, "soak", ct)) return limit;
            Note($"core {core}: {at} (limit {limit} + {_plan.SafetyMargin}) failed the {_plan.SoakSeconds} s soak; limit moves to {limit + Step}");
            limit += Step;
        }
        return null;
    }

    /// <summary>Every engine at this margin for this long; an invalid run (sleep, margin lost) repeats. False on the first positive.</summary>
    private bool Clean(int core, int margin, IReadOnlyList<string> engines, int seconds, string stage, CancellationToken ct)
    {
        foreach (var engine in engines)
        {
            if (ct.IsCancellationRequested) return false;
            RunResult r;
            do
            {
                r = RunOne(core, margin, engine, seconds, stage, ct);
                if (r.Verdict == "invalid") Note($"core {core} margin {margin} {engine} ({stage}): INVALID ({r.Error}); the run repeats");
            } while (r.Verdict == "invalid" && !ct.IsCancellationRequested);
            if (r.Verdict != "clean") return false;
        }
        return true;
    }

    private RunResult RunOne(int core, int margin, string engineName, int seconds, string stage, CancellationToken ct)
    {
        var started = DateTime.Now;
        _sink.RunStarted(core, margin, engineName, stage, seconds);

        Stepper.Apply(_co, [(core, margin)]);
        var read = _co.ReadCore(core);
        if (read != margin) throw new CoWriteFailedException($"core {core}: requested {margin}, hardware {read}");

        // The row is `running` from here: if the machine hangs or the process dies, the next start finds it.
        var runId = _store.BeginRun(_campaignId, core, margin, engineName, stage, started, BootTime());

        var work = Path.Combine(_o.CampaignDir, "work", $"core{core}");
        using var engine = new YCruncherEngine(_plan.YCruncherDir, engineName, _plan.Tests, suspend: _o.Suspend);
        var sampler = new Sampler(_tel, _pm, core);
        sampler.Prime();

        EngineStatus status;
        // A sleep of any kind mid-run resets every margin to the BIOS baseline and the
        // rest of the run tests nothing (it happened on 2026-08-31: a "-45 CLEAN after
        // 1096 s" that mostly ran at -5). Two independent detectors: a wall-clock jump
        // between loop iterations, and the margin no longer holding at the end.
        double gapSeconds = 0;
        var marginHeld = true;
        try
        {
            engine.Start(core, work);
            var deadline = started.AddSeconds(seconds);
            var lastLoop = DateTime.Now;
            while (true)
            {
                Thread.Sleep(1000);
                var loopGap = (DateTime.Now - lastLoop).TotalSeconds;
                lastLoop = DateTime.Now;
                if (loopGap > 30) { gapSeconds = loopGap; break; }
                var s = sampler.Take();
                status = engine.Poll();
                _store.AddSample(_campaignId, runId, core, margin, engineName, stage, s);
                _sink.Progress(s, status);
                if (status.State != EngineState.Running || DateTime.Now >= deadline || ct.IsCancellationRequested) break;
            }
            status = engine.Stop();
            marginHeld = _co.ReadCore(core) == margin;
        }
        finally
        {
            _co.TryRestore(_plan.Base);
        }

        Thread.Sleep(1500);   // the WHEA log takes a moment to be written
        var whea = Whea.HardwareSince(started);
        foreach (var e in Whea.IgnoredSince(started))
            Note($"core {core} margin {margin}: WHEA id {e.Id} during the run not counted (PCIe, not a core): {e.Message}");
        var invalid = gapSeconds > 0 || !marginHeld;
        var verdict =
            ct.IsCancellationRequested ? "aborted" :
            invalid ? "invalid" :   // before error/crash: whatever the engine did after a reset ran at the baseline, not at the margin
            status.State switch
            {
                EngineState.Error => "error",
                EngineState.Crashed => "crashed",
                _ when whea.Count > 0 => "whea",
                _ => "clean",
            };
        var error = invalid && verdict == "invalid"
            ? (gapSeconds > 0 ? $"wall-clock gap of {gapSeconds:F0} s (sleep?); margins untrustworthy" : "margin not held at the end of the run (reset to baseline?)")
            : status.Error ?? (whea.Count > 0 ? string.Join(" | ", whea.Select(e => $"{e.Provider.Replace("Microsoft-Windows-", "")} {e.Id}: {e.Message}")) : null);

        var result = new RunResult(core, margin, engineName, verdict, (int)(DateTime.Now - started).TotalSeconds, error, status.ExitCode,
            whea.Count, status.Lines, status.Suspensions, sampler.Summary(), started, DateTime.Now, stage);
        Record(runId, result);

        if (verdict is not ("clean" or "aborted" or "invalid"))
        {
            var dir = Path.Combine(_o.CampaignDir, "positives", $"core{core}-m{margin}-{Sanitize(engineName)}-{stage}-{started:HHmmss}");
            Directory.CreateDirectory(dir);
            if (File.Exists(engine.OutputPath)) File.Copy(engine.OutputPath, Path.Combine(dir, "output.txt"), overwrite: true);
            Journal.WriteJsonFile(Path.Combine(dir, "result.json"), result);
        }
        return result;
    }

    private void Record(long runId, RunResult r)
    {
        _store.EndRun(runId, r);
        _sink.RunEnded(r);
    }

    /// <summary>A line for the console and a row in the database.</summary>
    private void Note(string line, string kind = "info")
    {
        _store.AddEvent("sweep", null, _campaignId, DateTime.Now, kind, line);
        _sink.Event(line);
    }

    private static string Sanitize(string s) => new(s.Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Boot time from the tick counter; drifts by fractions of a second between calls, hence the tolerance in <see cref="SameBoot"/>. A run recorded without one (never, since 0.2.0) counts as a hang.</summary>
    internal static DateTime BootTime() => DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

    internal static bool SameBoot(DateTime? recorded, DateTime now)
        => recorded is { } r && Math.Abs((now - r).TotalSeconds) < 120;
}
