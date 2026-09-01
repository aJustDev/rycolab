using Rycolab.Core.Legion;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Rycolab.Core;

/// <summary>
/// SQLite database of a campaign (rycolab.db next to the JSONL files). The
/// JSONL is the primary source (write-through); the database is filled on
/// the fly and <see cref="Rebuild"/> regenerates it entirely from the JSONL.
/// </summary>
public sealed class Store : IDisposable
{
    private readonly SqliteConnection _db;

    public string Path { get; }

    public Store(string dbPath)
    {
        Path = dbPath;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dbPath)!);
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();
        Exec("""
            CREATE TABLE IF NOT EXISTS runs (
                id INTEGER PRIMARY KEY, core INT, margin INT, engine TEXT, verdict TEXT, seconds INT,
                error TEXT, exit_code INT, whea INT, lines INT, suspensions INT,
                volt REAL, ghz REAL, watts REAL, temp REAL, pkg_w REAL, clock REAL, clock_eff REAL,
                started TEXT, ended TEXT);
            CREATE TABLE IF NOT EXISTS samples (
                id INTEGER PRIMARY KEY, core INT, margin INT, engine TEXT, ts TEXT, elapsed INT,
                clock REAL, clock_eff REAL, volt REAL, ghz REAL, watts REAL, temp REAL, pkg_w REAL);
            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY, ts TEXT, kind TEXT, detail TEXT);
            CREATE TABLE IF NOT EXISTS ticks (
                id INTEGER PRIMARY KEY, ts TEXT, elapsed INT, ok INT, hardware TEXT, whea INT, cpu REAL, pkg_w REAL, state TEXT);
            CREATE TABLE IF NOT EXISTS health (
                id INTEGER PRIMARY KEY, ts TEXT, full_wh REAL, design_wh REAL, cycles INT);
            CREATE INDEX IF NOT EXISTS ix_runs_core ON runs(core, margin);
            CREATE INDEX IF NOT EXISTS ix_samples_run ON samples(core, margin, engine);
            """);
    }

    public void AddRun(RunResult r)
    {
        var t = r.Telemetry;
        Exec("""
            INSERT INTO runs (core, margin, engine, verdict, seconds, error, exit_code, whea, lines, suspensions,
                              volt, ghz, watts, temp, pkg_w, clock, clock_eff, started, ended)
            VALUES ($core, $margin, $engine, $verdict, $seconds, $error, $exit, $whea, $lines, $susp,
                    $volt, $ghz, $watts, $temp, $pkg, $clock, $eff, $started, $ended)
            """,
            ("$core", r.Core), ("$margin", r.Margin), ("$engine", r.Engine), ("$verdict", r.Verdict), ("$seconds", r.Seconds),
            ("$error", r.Error), ("$exit", r.ExitCode), ("$whea", r.Whea), ("$lines", r.Lines), ("$susp", r.Suspensions),
            ("$volt", t?.VoltMedian), ("$ghz", t?.FreqMedian), ("$watts", t?.PowerMedian), ("$temp", t?.TempMedian),
            ("$pkg", t?.PackagePowerMedian), ("$clock", t?.ClockMedian), ("$eff", t?.ClockEffectiveMedian),
            ("$started", r.Started.ToString("o")), ("$ended", r.Ended.ToString("o")));
    }

    public void AddSamples(int core, int margin, string engine, IEnumerable<Sample> samples)
    {
        using var tx = _db.BeginTransaction();
        foreach (var s in samples)
            Exec("""
                INSERT INTO samples (core, margin, engine, ts, elapsed, clock, clock_eff, volt, ghz, watts, temp, pkg_w)
                VALUES ($core, $margin, $engine, $ts, $el, $clock, $eff, $volt, $ghz, $watts, $temp, $pkg)
                """,
                ("$core", core), ("$margin", margin), ("$engine", engine), ("$ts", s.Ts.ToString("o")), ("$el", s.Elapsed),
                ("$clock", s.Clock), ("$eff", s.ClockEffective), ("$volt", s.Volt), ("$ghz", s.Freq), ("$watts", s.Power),
                ("$temp", s.Temp), ("$pkg", s.PackagePower));
        tx.Commit();
    }

    public void AddEvent(DateTime ts, string kind, string detail)
        => Exec("INSERT INTO events (ts, kind, detail) VALUES ($ts, $kind, $detail)", ("$ts", ts.ToString("o")), ("$kind", kind), ("$detail", detail));

    public void AddTick(GuardTick t)
        => Exec("INSERT INTO ticks (ts, elapsed, ok, hardware, whea, cpu, pkg_w, state) VALUES ($ts, $el, $ok, $hw, $whea, $cpu, $pkg, $state)",
            ("$ts", t.Ts.ToString("o")), ("$el", t.Elapsed), ("$ok", t.Ok ? 1 : 0), ("$hw", string.Join(",", t.Hardware.Select(h => h?.ToString() ?? "-"))),
            ("$whea", t.Whea), ("$cpu", t.CpuLoad), ("$pkg", t.PackagePower), ("$state", t.State));

    public void AddHealth(HealthSample s)
        => Exec("INSERT INTO health (ts, full_wh, design_wh, cycles) VALUES ($ts, $full, $design, $cycles)",
            ("$ts", s.Ts.ToString("o")), ("$full", s.FullWh), ("$design", s.DesignWh), ("$cycles", s.Cycles));

    public List<HealthSample> Health()
    {
        var list = new List<HealthSample>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT ts, full_wh, design_wh, cycles FROM health ORDER BY ts";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new HealthSample(DateTime.Parse(r.GetString(0)), D(r, 1), D(r, 2), r.IsDBNull(3) ? null : r.GetInt32(3)));
        return list;
    }

    public DateTime? LastHealthTs()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT MAX(ts) FROM health";
        return cmd.ExecuteScalar() is string s ? DateTime.Parse(s) : null;
    }

    public List<RunResult> Runs()
    {
        var list = new List<RunResult>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT core, margin, engine, verdict, seconds, error, exit_code, whea, lines, suspensions, volt, ghz, watts, temp, pkg_w, clock, clock_eff, started, ended FROM runs ORDER BY id";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var tele = r.IsDBNull(10) && r.IsDBNull(11) ? null
                : new SampleSummary(0, D(r, 15), D(r, 16), null, D(r, 10), null, D(r, 11), D(r, 12), D(r, 14), D(r, 13), null);
            list.Add(new RunResult(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetInt32(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetInt32(6), r.GetInt32(7), r.GetInt32(8), r.GetInt32(9),
                tele, DateTime.Parse(r.GetString(17)), DateTime.Parse(r.GetString(18))));
        }
        return list;
    }

    public List<(DateTime Ts, string Kind, string Detail)> Events()
    {
        var list = new List<(DateTime, string, string)>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT ts, kind, detail FROM events ORDER BY id";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((DateTime.Parse(r.GetString(0)), r.GetString(1), r.GetString(2)));
        return list;
    }

    /// <summary>Empties the database and refills it from runs.jsonl, samples.jsonl and guard.jsonl in the directory.</summary>
    public void Rebuild(string dir)
    {
        Exec("DELETE FROM runs; DELETE FROM samples; DELETE FROM events; DELETE FROM ticks; DELETE FROM health;");
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var runs = System.IO.Path.Combine(dir, "runs.jsonl");
        if (File.Exists(runs))
            foreach (var line in Lines(runs))
                AddRun(JsonSerializer.Deserialize<RunResult>(line, opts)!);

        var samples = System.IO.Path.Combine(dir, "samples.jsonl");
        if (File.Exists(samples))
        {
            using var tx = _db.BeginTransaction();
            foreach (var line in Lines(samples))
            {
                using var doc = JsonDocument.Parse(line);
                var e = doc.RootElement;
                Exec("""
                    INSERT INTO samples (core, margin, engine, ts, elapsed, clock, clock_eff, volt, ghz, watts, temp, pkg_w)
                    VALUES ($core, $margin, $engine, $ts, $el, $clock, $eff, $volt, $ghz, $watts, $temp, $pkg)
                    """,
                    ("$core", e.GetProperty("core").GetInt32()), ("$margin", e.GetProperty("margin").GetInt32()), ("$engine", e.GetProperty("engine").GetString()),
                    ("$ts", e.GetProperty("Ts").GetString()), ("$el", e.GetProperty("Elapsed").GetInt32()),
                    ("$clock", Num(e, "Clock")), ("$eff", Num(e, "ClockEffective")), ("$volt", Num(e, "Volt")), ("$ghz", Num(e, "Freq")),
                    ("$watts", Num(e, "Power")), ("$temp", Num(e, "Temp")), ("$pkg", Num(e, "PackagePower")));
            }
            tx.Commit();
        }

        var guard = System.IO.Path.Combine(dir, "guard.jsonl");
        if (File.Exists(guard))
            foreach (var line in Lines(guard))
            {
                using var doc = JsonDocument.Parse(line);
                var e = doc.RootElement;
                var kind = e.GetProperty("kind").GetString()!;
                if (kind == "tick")
                    AddTick(new GuardTick(e.GetProperty("Ts").GetDateTime(), e.GetProperty("Elapsed").GetInt32(), e.GetProperty("Ok").GetBoolean(),
                        e.GetProperty("Hardware").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetInt32() : (int?)null).ToArray(),
                        e.GetProperty("Whea").GetInt32(), Num(e, "CpuLoad"), Num(e, "PackagePower"), e.GetProperty("State").GetString()!));
                else if (kind == "health")
                    AddHealth(new HealthSample(e.GetProperty("ts").GetDateTime(), Num(e, "fullWh"), Num(e, "designWh"),
                        e.TryGetProperty("cycles", out var cy) && cy.ValueKind == JsonValueKind.Number ? cy.GetInt32() : null));
                else
                    AddEvent(e.GetProperty("ts").GetDateTime(), kind, e.GetProperty("detail").GetString() ?? "");
            }
    }

    /// <summary>Like File.ReadLines but shares with a live writer (the guard keeps its journal open).</summary>
    private static IEnumerable<string> Lines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        while (sr.ReadLine() is { } line)
            if (line.Trim().Length > 0) yield return line;
    }

    private static double? Num(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;

    private static double? D(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);

    private void Exec(string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
