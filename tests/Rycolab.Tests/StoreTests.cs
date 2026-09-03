using Rycolab.Core;
using Rycolab.Core.Legion;

namespace Rycolab.Tests;

public class StoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rycolab-test-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_dir, "rycolab.db");

    public StoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static readonly DateTime T0 = new(2026, 9, 1, 10, 0, 0);

    private static RunResult ARun(int core = 3, int margin = -45, string verdict = "error", string stage = "sweep") => new(
        core, margin, "24-ZN5 ~ Komari", verdict, 42, verdict == "error" ? "Checksum Mismatch" : null, verdict == "error" ? 1 : null, 0, 80, 4,
        new SampleSummary(40, 5165, 2630, 2600, 1.0675, 1.07, 5.167, 13.9, 45.2, 72.7, 74.0), T0, T0.AddSeconds(42), stage);

    [Fact]
    public void SchemaIsCreatedOnceAndReopens()
    {
        using (var s = new Store(DbPath)) Assert.Equal(Store.SchemaVersion.ToString(), s.Meta("schema_version"));
        using (var s = new Store(DbPath))
        {
            var (counts, bytes) = s.Stats();
            Assert.Equal(Store.Tables.Length, counts.Count);
            Assert.All(counts, c => Assert.Equal(0, c.Rows));
            Assert.True(bytes > 0);
        }
    }

    [Fact]
    public void CampaignRunSampleAndLimitRoundTrip()
    {
        using var s = new Store(DbPath);
        var id = s.OpenCampaign("find-1", _dir, new { seconds = 360 }, [0, 3], quick: false);
        Assert.Equal(id, s.OpenCampaign("find-1", _dir, new { }, [0], quick: true));   // by name: resumed, not duplicated
        Assert.Equal(id, s.CampaignId("find-1"));
        Assert.Null(s.CampaignId("find-2"));

        var runId = s.BeginRun(id, 3, -45, "24-ZN5 ~ Komari", "fine", T0, T0.AddHours(-1));
        s.AddSample(id, runId, 3, -45, "24-ZN5 ~ Komari", "fine", new Sample(T0.AddSeconds(1), 1, 5165, 2630, 1.0675, 5.167, 13.9, 72.7, 45.2, 80.1));
        Assert.Empty(s.Runs(id));   // still running
        var running = s.RunningRun(id)!;
        Assert.Equal(runId, running.Id);
        Assert.Equal("fine", running.Stage);
        Assert.Equal(T0.AddHours(-1), running.Boot);

        s.EndRun(runId, ARun(stage: "fine"));
        Assert.Null(s.RunningRun(id));
        var back = Assert.Single(s.Runs(id));
        Assert.Equal("error", back.Verdict);
        Assert.Equal("Checksum Mismatch", back.Error);
        Assert.Equal("fine", back.Stage);
        Assert.Equal(T0, back.Started);
        // The four fields the old store dropped survive now.
        Assert.Equal(40, back.Telemetry!.Samples);
        Assert.Equal(2600, back.Telemetry.ClockEffectiveP10);
        Assert.Equal(1.07, back.Telemetry.VoltMax);
        Assert.Equal(74.0, back.Telemetry.TempMax);

        s.SetLimit(id, 3, -40);
        s.SetLimit(id, 0, null);
        s.SetLimit(id, 3, -35);   // a core closes once; the last word wins
        var limits = s.Limits(id);
        Assert.Equal(2, limits.Count);
        Assert.Equal(-35, limits[3]);
        Assert.Null(limits[0]);
        var all = s.AllLimits();
        Assert.Equal(2, all.Count);
        Assert.Equal("find-1", all[0].Campaign);

        var (cols, rows) = s.Query("SELECT core, stage, tctl FROM samples");
        Assert.Equal(["core", "stage", "tctl"], cols);
        Assert.Equal(80.1, Assert.Single(rows)[2]);
        Assert.Single(s.Campaigns());
    }

    [Fact]
    public void SessionTicksEventsAndHealth()
    {
        using var s = new Store(DbPath);
        var session = s.BeginSession(1234, "-40,-40", 60, adhoc: false, T0);
        var extras = new TickExtras(false, 12.5, 80.0, 64.0, 80.0, 55, 40, 48, 2100, 0, 1800, 1, 2, 60, 40);
        s.AddTick(session, new GuardTick(T0.AddMinutes(1), 60, true, [-40, null], 0, 3.5, 12.0, "ok", extras));
        s.AddTick(session, new GuardTick(T0.AddMinutes(2), 120, true, [-40, -40], 0, null, null, "ok"));   // no extras: every column null
        s.AddEvent("guard", session, null, T0, "start", "profile -40,-40");
        s.AddHealth(new HealthSample(T0, 80.5, 99.9, 12));
        s.EndSession(session, 0, T0.AddMinutes(3));

        var ticks = s.Ticks(T0.AddSeconds(30));
        Assert.Equal(2, ticks.Count);
        Assert.Equal(session, ticks[0].SessionId);
        Assert.Equal([-40, null], ticks[0].Tick.Hardware);
        Assert.Equal(extras, ticks[0].Tick.Extras);
        Assert.Equal(TickExtras.Empty, ticks[1].Tick.Extras);
        Assert.Single(s.Ticks(T0.AddSeconds(90)));
        Assert.Single(s.Ticks(T0, T0.AddSeconds(90)));

        var sess = Assert.Single(s.Sessions());
        Assert.Equal(T0.AddMinutes(3), sess.Ended);
        Assert.Equal(0, sess.ExitCode);
        Assert.False(sess.Adhoc);

        var ev = Assert.Single(s.Events("guard"));
        Assert.Equal("start", ev.Kind);
        Assert.Empty(s.Events("sweep"));

        var health = Assert.Single(s.Health());
        Assert.Equal(80.5, health.FullWh);
        Assert.Equal(12, health.Cycles);
        Assert.Equal(T0, s.LastHealthTs());
    }

    [Fact]
    public void QueryIsReadOnlyAndExportKnowsItsTables()
    {
        using var s = new Store(DbPath);
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => s.Query("INSERT INTO health (ts) VALUES ('x')"));
        Assert.Empty(s.Health());
        s.AddHealth(new HealthSample(T0, 80.5, 99.9, 12));   // the connection writes again after a read-only query
        Assert.Throws<ArgumentException>(() => s.Export("sqlite_master", null));
        Assert.Single(s.Export("health", T0.AddDays(-1)).Rows);
        Assert.Empty(s.Export("health", T0.AddDays(1)).Rows);
    }

    [Fact]
    public void ImportsTheJsonlEraOnce()
    {
        // The files the 0.1/0.2 Journal wrote, verbatim shapes.
        var campaign = Path.Combine(_dir, "campaigns", "find-20260828-1232");
        Directory.CreateDirectory(campaign);
        File.WriteAllText(Path.Combine(campaign, "campaign.json"), """{"started":"2026-08-28T12:32:00","plan":{"Seconds":180},"cores":[13,3]}""");
        File.WriteAllText(Path.Combine(campaign, "runs.jsonl"),
            """{"Core":3,"Margin":-45,"Engine":"24-ZN5 ~ Komari","Verdict":"error","Seconds":42,"Error":"Checksum Mismatch","ExitCode":1,"Whea":0,"Lines":80,"Suspensions":4,"Telemetry":{"Samples":40,"ClockMedian":5165,"ClockEffectiveMedian":2630,"ClockEffectiveP10":2600,"VoltMedian":1.0675,"VoltMax":1.07,"FreqMedian":5.167,"PowerMedian":13.9,"PackagePowerMedian":45.2,"TempMedian":72.7,"TempMax":74.0},"Started":"2026-09-01T10:00:00","Ended":"2026-09-01T10:00:42"}""" + "\n" +
            """{"Core":3,"Margin":-40,"Engine":"24-ZN5 ~ Komari","Verdict":"clean","Seconds":360,"Error":null,"ExitCode":null,"Whea":0,"Lines":90,"Suspensions":35,"Telemetry":null,"Started":"2026-09-01T10:01:00","Ended":"2026-09-01T10:07:00","Stage":"fine"}""" + "\n");
        File.WriteAllText(Path.Combine(campaign, "samples.jsonl"),
            """{"core":3,"margin":-45,"engine":"24-ZN5 ~ Komari","Ts":"2026-09-01T10:00:01","Elapsed":1,"Clock":5165,"ClockEffective":2630,"Volt":1.0675,"Freq":5.167,"Power":13.9,"Temp":72.7,"PackagePower":45.2}""" + "\n" +
            """{"core":3,"margin":-40,"engine":"24-ZN5 ~ Komari","stage":"fine","Ts":"2026-09-01T10:01:05","Elapsed":5,"Clock":5165,"ClockEffective":2630,"Volt":1.06,"Freq":5.1,"Power":12.0,"Temp":70.0,"PackagePower":40.0}""" + "\n" +
            """{"core":9,"margin":-50,"engine":"04-P4P","Ts":"2026-09-01T11:00:00","Elapsed":1,"Clock":5450}""" + "\n");   // no run for it: kept, run_id 0
        File.WriteAllText(Path.Combine(campaign, "limits.json"), """{"3":-40,"13":null}""");

        var guard = Path.Combine(_dir, "guard");
        Directory.CreateDirectory(guard);
        File.WriteAllText(Path.Combine(guard, "guard.jsonl"),
            """{"kind":"start","ts":"2026-09-01T10:00:00","detail":"profile -40,-40  interval 60s  no time limit"}""" + "\n" +
            """{"kind":"tick","Ts":"2026-09-01T10:01:00","Elapsed":60,"Ok":true,"Hardware":[-40,null],"Whea":0,"CpuLoad":3.5,"PackagePower":12.0,"State":"ok"}""" + "\n" +
            """{"kind":"health","ts":"2026-09-01T10:01:00","fullWh":80.5,"designWh":99.9,"cycles":12}""" + "\n" +
            """{"kind":"restore","ts":"2026-09-01T10:05:00","detail":"baseline -5: 16 cores written; hardware -5,-5  code 0"}""" + "\n" +
            """{"kind":"start","ts":"2026-09-02T09:00:00","detail":"profile -40,-40  interval 30s  no time limit"}""" + "\n" +
            """{"kind":"tick","Ts":"2026-09-02T09:00:30","Elapsed":30,"Ok":true,"Hardware":[-40,-40],"Whea":0,"CpuLoad":1.0,"PackagePower":null,"State":"ok"}""" + "\n");

        using var s = new Store(DbPath);
        var report = s.ImportLegacy(_dir);
        Assert.Equal(2, report.Count);
        Assert.Contains("2 sessions, 2 ticks, 3 events, 1 health", report[0]);
        Assert.Contains("campaign find-20260828-1232, 2 runs, 3 samples, 2 limits", report[1]);

        var c = Assert.Single(s.Campaigns());
        Assert.Equal("find-20260828-1232", c.Name);
        Assert.Equal(new DateTime(2026, 8, 28, 12, 32, 0), c.Started);
        Assert.Equal(new DateTime(2026, 9, 1, 10, 7, 0), c.Ended);
        Assert.Equal("13,3", c.Cores);
        var runs = s.Runs(c.Id);
        Assert.Equal(2, runs.Count);
        Assert.Equal("sweep", runs[0].Stage);
        Assert.Equal(1.07, runs[0].Telemetry!.VoltMax);
        Assert.Equal("fine", runs[1].Stage);
        Assert.Null(runs[1].Telemetry);
        var limits = s.Limits(c.Id);
        Assert.Equal(-40, limits[3]);
        Assert.Null(limits[13]);
        // Samples hang off the run whose window holds them; the orphan keeps its data with run 0.
        var (_, rows) = s.Query("SELECT run_id, stage FROM samples ORDER BY id");
        Assert.Equal(3, rows.Count);
        Assert.NotEqual(0L, rows[0][0]);
        Assert.Equal("sweep", rows[0][1]);
        Assert.Equal("fine", rows[1][1]);
        Assert.Equal(0L, rows[2][0]);

        var sessions = s.Sessions();
        Assert.Equal(2, sessions.Count);
        Assert.Equal(new DateTime(2026, 9, 1, 10, 5, 0), sessions[0].Ended);
        Assert.Equal(0, sessions[0].ExitCode);
        Assert.Equal("-40,-40", sessions[0].Profile);
        Assert.Equal(30, sessions[1].Interval);
        Assert.Null(sessions[1].Ended);
        var ticks = s.Ticks(DateTime.MinValue);
        Assert.Equal(sessions[0].Id, ticks[0].SessionId);
        Assert.Equal(sessions[1].Id, ticks[1].SessionId);
        Assert.Equal(TickExtras.Empty, ticks[0].Tick.Extras);
        Assert.Equal(3, s.Events("guard").Count);
        Assert.Single(s.Health());

        // Again: nothing doubles.
        var again = s.ImportLegacy(_dir);
        Assert.Contains("already up to date (6 lines)", again[0]);
        Assert.Contains("already imported", again[1]);
        Assert.Equal(2, s.Runs(c.Id).Count);
        Assert.Equal(2, s.Sessions().Count);
        Assert.Single(s.Campaigns());

        // The old guard kept writing: the new lines come in, on the session that was open.
        File.AppendAllText(Path.Combine(guard, "guard.jsonl"),
            """{"kind":"tick","Ts":"2026-09-02T09:01:00","Elapsed":60,"Ok":true,"Hardware":[-40,-40],"Whea":0,"CpuLoad":1.0,"PackagePower":null,"State":"ok"}""" + "\n" +
            """{"kind":"restore","ts":"2026-09-02T09:02:00","detail":"baseline -5: 16 cores written; hardware -5,-5  code 10"}""" + "\n");
        var more = s.ImportLegacy(_dir);
        Assert.Contains("0 sessions, 1 ticks, 1 events, 0 health samples (lines 7-8)", more[0]);
        Assert.Equal(3, s.Ticks(DateTime.MinValue).Count);
        Assert.Equal(sessions[1].Id, s.Ticks(DateTime.MinValue)[2].SessionId);
        Assert.Equal(10, s.Sessions()[1].ExitCode);
        Assert.Equal(new DateTime(2026, 9, 2, 9, 2, 0), s.Sessions()[1].Ended);
    }

    [Fact]
    public void ImportWithNothingSaysSo()
    {
        using var s = new Store(DbPath);
        Assert.Equal(["nothing to import"], s.ImportLegacy(_dir));
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
