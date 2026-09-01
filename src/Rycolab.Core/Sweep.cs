using Rycolab.Core.Engines;

namespace Rycolab.Core;

public sealed record RunResult(
    int Core, int Margin, string Engine, string Verdict, int Seconds, string? Error, int? ExitCode,
    int Whea, int Lines, int Suspensions, SampleSummary? Telemetry, DateTime Started, DateTime Ended);

public sealed class SweepOptions
{
    public int[] Cores { get; init; } = Enumerable.Range(0, Topology.MaxCores).ToArray();
    public int? Start { get; init; }
    public int? Top { get; init; }
    public int? Step { get; init; }
    public int? Seconds { get; init; }
    public bool Suspend { get; init; } = true;
    public required string CampaignDir { get; init; }
}

public interface ISweepSink
{
    void RunStarted(int core, int margin, string engine);
    void Progress(Sample sample, EngineStatus status);
    void RunEnded(RunResult result);
    void CoreDone(int core, int? limit);
    void Event(string line);
}

/// <summary>
/// Per-core sweep. For each core, from the start margin upwards step by
/// step; for each margin, every engine in the plan; the limit is the first
/// margin that is clean on all of them. Any positive (error, crash, WHEA,
/// hang) moves one step up. Every run restores the baseline. Resumable:
/// limits.json holds the cores already done and in-progress.json betrays a
/// machine hang.
/// </summary>
public sealed class Sweep
{
    private readonly CoController _co;
    private readonly Plan _plan;
    private readonly SweepOptions _o;
    private readonly Telemetry? _tel;
    private readonly PmTable? _pm;
    private readonly ISweepSink _sink;
    private readonly Journal _runs;
    private readonly Journal _samples;
    private readonly Store _store;
    private readonly string _limitsPath;
    private readonly string _inProgressPath;

    public Dictionary<int, int?> Limits { get; } = [];

    public Sweep(CoController co, Plan plan, SweepOptions options, Telemetry? telemetry, PmTable? pm, ISweepSink sink)
    {
        _co = co; _plan = plan; _o = options; _tel = telemetry; _pm = pm; _sink = sink;
        Directory.CreateDirectory(_o.CampaignDir);
        _runs = new Journal(Path.Combine(_o.CampaignDir, "runs.jsonl"));
        _samples = new Journal(Path.Combine(_o.CampaignDir, "samples.jsonl"));
        _store = new Store(Path.Combine(_o.CampaignDir, "rycolab.db"));
        _limitsPath = Path.Combine(_o.CampaignDir, "limits.json");
        _inProgressPath = Path.Combine(_o.CampaignDir, "in-progress.json");

        var existing = Journal.ReadJsonFile<Dictionary<string, int?>>(_limitsPath);
        if (existing is not null)
            foreach (var (k, v) in existing) Limits[int.Parse(k)] = v;

        Journal.WriteJsonFile(Path.Combine(_o.CampaignDir, "campaign.json"), new
        {
            started = DateTime.Now, plan = _plan, cores = _o.Cores, start = Start, top = Top, step = Step, seconds = Seconds, suspend = _o.Suspend
        });
    }

    private int Start => _o.Start ?? _plan.Start;
    private int Top => _o.Top ?? _plan.Top;
    private int Step => _o.Step ?? _plan.Step;
    private int Seconds => _o.Seconds ?? _plan.Seconds;

    public int Run(CancellationToken ct)
    {
        var t0 = DateTime.Now;
        _sink.Event($"sweep: cores {string.Join(",", _o.Cores)}  {Start} -> {Top} step {Step}  {Seconds} s  engines {string.Join(" | ", _plan.Engines)}  tests {string.Join(",", _plan.Tests)}");

        // A y-cruncher load does not count as user activity: without this the
        // machine slept mid-run on 2026-08-31 (AC standby 1 h) and the run
        // closed as a false CLEAN at the baseline.
        KeepAwake.On();

        // A run left in progress: a machine hang (the BIOS restored the baseline by itself) if
        // the machine has rebooted since; if it is still the same boot session, the process was
        // killed (console closed, session ended) and the run simply repeats (2026-08-31 15:45).
        var hang = Journal.ReadJsonFile<InProgress>(_inProgressPath);
        InProgress? killed = null;
        if (hang is not null)
        {
            File.Delete(_inProgressPath);
            if (SameBoot(hang.Boot, BootTime()))
            {
                killed = hang;
                hang = null;
                _sink.Event($"RUN IN PROGRESS AT STARTUP: core {killed.Core} margin {killed.Margin} {killed.Engine} since {killed.Ts:HH:mm:ss}, same boot session -> the process was killed, not a hang; the run repeats");
                Record(new RunResult(killed.Core, killed.Margin, killed.Engine, "aborted", 0, "process killed (same boot session)", null, 0, 0, 0, null, killed.Ts, DateTime.Now));
            }
            else
            {
                _sink.Event($"RUN IN PROGRESS AT STARTUP: core {hang.Core} margin {hang.Margin} {hang.Engine} since {hang.Ts:HH:mm:ss} -> machine hang, positive");
                Record(new RunResult(hang.Core, hang.Margin, hang.Engine, "hang", 0, "machine hang (reboot)", null, 0, 0, 0, null, hang.Ts, DateTime.Now));
            }
        }

        try
        {
            foreach (var core in _o.Cores)
            {
                if (ct.IsCancellationRequested) break;
                if (Limits.TryGetValue(core, out var done) && done is not null)
                {
                    _sink.Event($"core {core}: already has limit {done}, skipped");
                    continue;
                }

                // Every margin below the one in progress was a positive (a clean one would have closed the core).
                var from = Start;
                if (hang is not null && hang.Core == core) from = Math.Min(Top, hang.Margin + Step);
                if (killed is not null && killed.Core == core) from = killed.Margin;

                int? limit = null;
                for (var m = from; m <= Top; m += Step)
                {
                    if (ct.IsCancellationRequested) break;
                    var clean = true;
                    foreach (var engine in _plan.Engines)
                    {
                        if (ct.IsCancellationRequested) { clean = false; break; }
                        RunResult r;
                        do
                        {
                            r = RunOne(core, m, engine, ct);
                            if (r.Verdict == "invalid") _sink.Event($"core {core} margin {m} {engine}: INVALID ({r.Error}); the run repeats");
                        } while (r.Verdict == "invalid" && !ct.IsCancellationRequested);
                        if (r.Verdict != "clean") { clean = false; break; }
                    }
                    if (clean) { limit = m; break; }
                }
                if (ct.IsCancellationRequested) break;

                Limits[core] = limit;
                Journal.WriteJsonFile(_limitsPath, Limits.OrderBy(k => k.Key).ToDictionary(k => k.Key.ToString(), k => k.Value));
                _sink.CoreDone(core, limit);
            }
        }
        finally
        {
            KeepAwake.Off();
            _co.TryRestore(_plan.Base);
            _runs.Dispose();
            _samples.Dispose();
            _store.Dispose();
        }

        _sink.Event(ct.IsCancellationRequested
            ? $"interrupted after {(DateTime.Now - t0).TotalMinutes:F0} min; baseline {_plan.Base} restored"
            : $"sweep finished in {(DateTime.Now - t0).TotalMinutes:F0} min");
        return ct.IsCancellationRequested ? 1 : 0;
    }

    private RunResult RunOne(int core, int margin, string engineName, CancellationToken ct)
    {
        var started = DateTime.Now;
        _sink.RunStarted(core, margin, engineName);

        Stepper.Apply(_co, [(core, margin)]);
        var read = _co.ReadCore(core);
        if (read != margin) throw new CoWriteFailedException($"core {core}: requested {margin}, hardware {read}");

        Journal.WriteJsonFile(_inProgressPath, new InProgress(core, margin, engineName, started, BootTime()));

        var work = Path.Combine(_o.CampaignDir, "work", $"core{core}");
        using var engine = new YCruncherEngine(_plan.YCruncherDir, engineName, _plan.Tests, suspend: _o.Suspend);
        var sampler = new Sampler(_tel, _pm, core);
        sampler.Prime();

        EngineStatus status;
        var taken = new List<Sample>();
        // A sleep of any kind mid-run resets every margin to the BIOS baseline and the
        // rest of the run tests nothing (it happened on 2026-08-31: a "-45 CLEAN after
        // 1096 s" that mostly ran at -5). Two independent detectors: a wall-clock jump
        // between loop iterations, and the margin no longer holding at the end.
        double gapSeconds = 0;
        var marginHeld = true;
        try
        {
            engine.Start(core, work);
            var deadline = started.AddSeconds(Seconds);
            var lastLoop = DateTime.Now;
            while (true)
            {
                Thread.Sleep(1000);
                var loopGap = (DateTime.Now - lastLoop).TotalSeconds;
                lastLoop = DateTime.Now;
                if (loopGap > 30) { gapSeconds = loopGap; break; }
                var s = sampler.Take();
                status = engine.Poll();
                taken.Add(s);
                _samples.Write(new { core, margin, engine = engineName, s.Ts, s.Elapsed, s.Clock, s.ClockEffective, s.Volt, s.Freq, s.Power, s.Temp, s.PackagePower });
                _sink.Progress(s, status);
                if (status.State != EngineState.Running || DateTime.Now >= deadline || ct.IsCancellationRequested) break;
            }
            status = engine.Stop();
            marginHeld = _co.ReadCore(core) == margin;
        }
        finally
        {
            _co.TryRestore(_plan.Base);
            File.Delete(_inProgressPath);
        }

        Thread.Sleep(1500);   // the WHEA log takes a moment to be written
        var whea = Whea.HardwareSince(started);
        foreach (var e in Whea.IgnoredSince(started))
            _sink.Event($"core {core} margin {margin}: WHEA id {e.Id} during the run not counted (PCIe, not a core): {e.Message}");
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
            whea.Count, status.Lines, status.Suspensions, sampler.Summary(), started, DateTime.Now);
        Record(result);
        _store.AddSamples(core, margin, engineName, taken);

        if (verdict is not ("clean" or "aborted" or "invalid"))
        {
            var dir = Path.Combine(_o.CampaignDir, "positives", $"core{core}-m{margin}-{Sanitize(engineName)}-{started:HHmmss}");
            Directory.CreateDirectory(dir);
            if (File.Exists(engine.OutputPath)) File.Copy(engine.OutputPath, Path.Combine(dir, "output.txt"), overwrite: true);
            Journal.WriteJsonFile(Path.Combine(dir, "result.json"), result);
        }
        return result;
    }

    private void Record(RunResult r)
    {
        _runs.Write(r);
        _store.AddRun(r);
        _sink.RunEnded(r);
    }

    private static string Sanitize(string s) => new(s.Where(char.IsLetterOrDigit).ToArray());

    /// <summary><paramref name="Boot"/>: when the machine booted, so a file left behind can be told apart from a hang. Null on files from before it was recorded (treated as a hang, the old behaviour).</summary>
    internal sealed record InProgress(int Core, int Margin, string Engine, DateTime Ts, DateTime? Boot = null);

    /// <summary>Boot time from the tick counter; drifts by fractions of a second between calls, hence the tolerance in <see cref="SameBoot"/>.</summary>
    internal static DateTime BootTime() => DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

    internal static bool SameBoot(DateTime? recorded, DateTime now)
        => recorded is { } r && Math.Abs((now - r).TotalSeconds) < 120;
}
