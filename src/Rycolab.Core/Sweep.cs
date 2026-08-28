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

        // Machine hang during the previous run: the BIOS restored the baseline by itself.
        var hang = Journal.ReadJsonFile<InProgress>(_inProgressPath);
        if (hang is not null)
        {
            File.Delete(_inProgressPath);
            _sink.Event($"RUN IN PROGRESS AT STARTUP: core {hang.Core} margin {hang.Margin} {hang.Engine} since {hang.Ts:HH:mm:ss} -> machine hang, positive");
            Record(new RunResult(hang.Core, hang.Margin, hang.Engine, "hang", 0, "machine hang (reboot)", null, 0, 0, 0, null, hang.Ts, DateTime.Now));
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

                var from = Start;
                if (hang is not null && hang.Core == core) from = Math.Min(Top, hang.Margin + Step);

                int? limit = null;
                for (var m = from; m <= Top; m += Step)
                {
                    if (ct.IsCancellationRequested) break;
                    var clean = true;
                    foreach (var engine in _plan.Engines)
                    {
                        if (ct.IsCancellationRequested) { clean = false; break; }
                        var r = RunOne(core, m, engine, ct);
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

        Journal.WriteJsonFile(_inProgressPath, new InProgress(core, margin, engineName, started));

        var work = Path.Combine(_o.CampaignDir, "work", $"core{core}");
        using var engine = new YCruncherEngine(_plan.YCruncherDir, engineName, _plan.Tests, suspend: _o.Suspend);
        var sampler = new Sampler(_tel, _pm, core);
        sampler.Prime();

        EngineStatus status;
        var taken = new List<Sample>();
        try
        {
            engine.Start(core, work);
            var deadline = started.AddSeconds(Seconds);
            while (true)
            {
                Thread.Sleep(1000);
                var s = sampler.Take();
                status = engine.Poll();
                taken.Add(s);
                _samples.Write(new { core, margin, engine = engineName, s.Ts, s.Elapsed, s.Clock, s.ClockEffective, s.Volt, s.Freq, s.Power, s.Temp, s.PackagePower });
                _sink.Progress(s, status);
                if (status.State != EngineState.Running || DateTime.Now >= deadline || ct.IsCancellationRequested) break;
            }
            status = engine.Stop();
        }
        finally
        {
            _co.TryRestore(_plan.Base);
            File.Delete(_inProgressPath);
        }

        Thread.Sleep(1500);   // the WHEA log takes a moment to be written
        var whea = Whea.HardwareSince(started);
        var verdict = status.State switch
        {
            EngineState.Error => "error",
            EngineState.Crashed => "crashed",
            _ when whea.Count > 0 => "whea",
            _ when ct.IsCancellationRequested => "aborted",
            _ => "clean",
        };
        var error = status.Error ?? (whea.Count > 0 ? string.Join(" | ", whea.Select(e => $"{e.Provider.Replace("Microsoft-Windows-", "")} {e.Id}: {e.Message}")) : null);

        var result = new RunResult(core, margin, engineName, verdict, (int)(DateTime.Now - started).TotalSeconds, error, status.ExitCode,
            whea.Count, status.Lines, status.Suspensions, sampler.Summary(), started, DateTime.Now);
        Record(result);
        _store.AddSamples(core, margin, engineName, taken);

        if (verdict is not ("clean" or "aborted"))
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

    private sealed record InProgress(int Core, int Margin, string Engine, DateTime Ts);
}
