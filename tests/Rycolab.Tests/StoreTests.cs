using Rycolab.Core;

namespace Rycolab.Tests;

public class StoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rycolab-test-{Guid.NewGuid():N}");

    public StoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void RebuildFromJournalsRoundTrips()
    {
        var started = new DateTime(2026, 9, 1, 10, 0, 0);
        var run = new RunResult(3, -45, "24-ZN5 ~ Komari", "error", 42, "Checksum Mismatch", 1, 0, 80, 4,
            new SampleSummary(40, 5165, 2630, 2600, 1.0675, 1.07, 5.167, 13.9, 45.2, 72.7, 74.0), started, started.AddSeconds(42));
        var sample = new Sample(started.AddSeconds(1), 1, 5165, 2630, 1.0675, 5.167, 13.9, 72.7, 45.2, 80.1);

        using (var runs = new Journal(Path.Combine(_dir, "runs.jsonl"))) runs.Write(run);
        using (var samples = new Journal(Path.Combine(_dir, "samples.jsonl")))
            samples.Write(new { core = 3, margin = -45, engine = "24-ZN5 ~ Komari", sample.Ts, sample.Elapsed, sample.Clock, sample.ClockEffective, sample.Volt, sample.Freq, sample.Power, sample.Temp, sample.PackagePower });
        using (var guard = new Journal(Path.Combine(_dir, "guard.jsonl")))
        {
            guard.Write(new { kind = "start", ts = started, detail = "profile -40" });
            guard.Write(new { kind = "tick", Ts = started.AddMinutes(1), Elapsed = 60, Ok = true, Hardware = new int?[] { -40, null }, Whea = 0, CpuLoad = 3.5, PackagePower = 12.0, State = "ok" });
            guard.Write(new { kind = "health", ts = started, fullWh = 80.5, designWh = 99.9, cycles = 12 });
        }

        using var store = new Store(Path.Combine(_dir, "rycolab.db"));
        store.Rebuild(_dir);

        var back = Assert.Single(store.Runs());
        Assert.Equal(run.Core, back.Core);
        Assert.Equal(run.Margin, back.Margin);
        Assert.Equal(run.Verdict, back.Verdict);
        Assert.Equal(run.Error, back.Error);
        Assert.Equal(run.Telemetry!.VoltMedian, back.Telemetry!.VoltMedian);
        Assert.Equal(started, back.Started);

        var ev = Assert.Single(store.Events());
        Assert.Equal("start", ev.Kind);

        var health = Assert.Single(store.Health());
        Assert.Equal(80.5, health.FullWh);
        Assert.Equal(12, health.Cycles);
        Assert.Equal(started, store.LastHealthTs());
    }

    [Fact]
    public void RebuildIsIdempotent()
    {
        using (var runs = new Journal(Path.Combine(_dir, "runs.jsonl")))
            runs.Write(new RunResult(0, -50, "04-P4P", "clean", 360, null, null, 0, 90, 35, null, DateTime.Now, DateTime.Now));
        using var store = new Store(Path.Combine(_dir, "rycolab.db"));
        store.Rebuild(_dir);
        store.Rebuild(_dir);
        Assert.Single(store.Runs());
    }
}

public class JournalTests
{
    [Fact]
    public void WriteJsonFileIsAtomicAndReadable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rycolab-test-{Guid.NewGuid():N}.json");
        try
        {
            Journal.WriteJsonFile(path, new Dictionary<string, int?> { ["0"] = -40, ["1"] = null });
            Assert.False(File.Exists(path + ".tmp"));
            var back = Journal.ReadJsonFile<Dictionary<string, int?>>(path)!;
            Assert.Equal(-40, back["0"]);
            Assert.Null(back["1"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingOrBrokenFileReadsAsDefault()
    {
        Assert.Null(Journal.ReadJsonFile<Profile>(Path.Combine(Path.GetTempPath(), "rycolab-does-not-exist.json")));
        var path = Path.Combine(Path.GetTempPath(), $"rycolab-test-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not json");
            Assert.Null(Journal.ReadJsonFile<Profile>(path));
        }
        finally { File.Delete(path); }
    }
}
