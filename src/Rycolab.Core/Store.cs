using Rycolab.Core.Legion;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Rycolab.Core;

/// <summary>
/// What a guard tick knows beyond the margins: battery, Lenovo EC, power and
/// GPU mode, panel; and since 0.4.0 the cores from the PM table (hottest
/// core, mean voltage, highest clock), the user's idle time, charging (W and
/// mode), the dGPU on the bus, the Windows overlay and the SMU read latency.
/// Null where the machine has no such thing.
/// </summary>
public sealed record TickExtras(
    bool? Ac, double? BatW, double? BatPct, double? BatWh, double? BatFullWh,
    int? EcCpuC, int? EcGpuC, int? EcPchC, int? FanCpu, int? FanGpu, int? FanPch,
    int? PowerMode, int? GpuMode, int? Hz, int? Brightness,
    double? CoreTempMax = null, int? CoreHot = null, double? CoreVoltMean = null, double? CoreGhzMax = null,
    int? IdleS = null, double? ChargeW = null, string? ChargeMode = null, bool? Dgpu = null, string? Overlay = null, int? SmuMs = null)
{
    public static readonly TickExtras Empty = new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
}

public sealed record CampaignRow(long Id, string Name, string? Dir, DateTime Started, DateTime? Ended, string? Plan, string Cores, bool Quick);
public sealed record SessionRow(long Id, DateTime Started, DateTime? Ended, int? Pid, string? Profile, int Interval, bool Adhoc, int? ExitCode);
public sealed record TickRow(long Id, long? SessionId, GuardTick Tick);
public sealed record RunningRun(long Id, int Core, int Margin, string Engine, string Stage, DateTime Started, DateTime? Boot);
public sealed record LimitRow(long CampaignId, string Campaign, int Core, int? Margin, DateTime Ts);

/// <summary>
/// The one database: %LOCALAPPDATA%\rycolab\rycolab.db, the history of
/// everything rycolab measures or does (campaigns, runs, samples, limits,
/// guard sessions, ticks, events, battery health, bench logs). WAL so the
/// unelevated `report` reads while the guard writes; synchronous=FULL so a
/// row is on disk when the call returns (a hang keeps the last seconds).
/// Schema changes are additive: a new column is an ALTER TABLE in
/// <see cref="Migrate"/>, never a rebuild.
/// </summary>
public sealed class Store : IDisposable
{
    public const int SchemaVersion = 3;

    public static readonly string[] Tables =
        ["campaigns", "runs", "samples", "limits", "sessions", "ticks", "events", "health", "bench", "bench_samples"];

    private readonly SqliteConnection _db;

    public string Path { get; }

    public static Store Open() => new(AppPaths.Db);

    public Store(string dbPath)
    {
        Path = dbPath;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dbPath)!);
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;");
        Exec("""
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS campaigns (
                id INTEGER PRIMARY KEY, name TEXT UNIQUE, dir TEXT, started TEXT, ended TEXT, plan TEXT, cores TEXT, quick INT);
            CREATE TABLE IF NOT EXISTS runs (
                id INTEGER PRIMARY KEY, campaign_id INT, core INT, margin INT, engine TEXT, stage TEXT, verdict TEXT, seconds INT,
                error TEXT, exit_code INT, whea INT, lines INT, suspensions INT, samples INT,
                volt REAL, volt_max REAL, ghz REAL, watts REAL, temp REAL, temp_max REAL, pkg_w REAL,
                clock REAL, clock_eff REAL, clock_eff_p10 REAL, started TEXT, ended TEXT, boot TEXT);
            CREATE TABLE IF NOT EXISTS samples (
                id INTEGER PRIMARY KEY, campaign_id INT, run_id INT, core INT, margin INT, engine TEXT, stage TEXT, ts TEXT, elapsed INT,
                clock REAL, clock_eff REAL, volt REAL, ghz REAL, watts REAL, temp REAL, pkg_w REAL, tctl REAL);
            CREATE TABLE IF NOT EXISTS limits (
                campaign_id INT, core INT, margin INT, ts TEXT, PRIMARY KEY (campaign_id, core));
            CREATE TABLE IF NOT EXISTS sessions (
                id INTEGER PRIMARY KEY, started TEXT, ended TEXT, pid INT, profile TEXT, interval INT, adhoc INT, exit_code INT);
            CREATE TABLE IF NOT EXISTS ticks (
                id INTEGER PRIMARY KEY, session_id INT, ts TEXT, elapsed INT, ok INT, hardware TEXT, whea INT, cpu REAL, pkg_w REAL, state TEXT,
                ac INT, bat_w REAL, bat_pct REAL, bat_wh REAL, bat_full_wh REAL,
                ec_cpu_c INT, ec_gpu_c INT, ec_pch_c INT, fan_cpu INT, fan_gpu INT, fan_pch INT,
                power_mode INT, gpu_mode INT, hz INT, brightness INT,
                core_temp_max REAL, core_hot INT, core_volt_mean REAL, core_ghz_max REAL,
                idle_s INT, charge_w REAL, charge_mode TEXT, dgpu INT, overlay TEXT, smu_ms INT);
            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY, ts TEXT, source TEXT, session_id INT, campaign_id INT, kind TEXT, detail TEXT);
            CREATE TABLE IF NOT EXISTS health (
                id INTEGER PRIMARY KEY, ts TEXT, full_wh REAL, design_wh REAL, cycles INT);
            CREATE TABLE IF NOT EXISTS bench (
                id INTEGER PRIMARY KEY, name TEXT, started TEXT, ended TEXT, interval INT);
            CREATE TABLE IF NOT EXISTS bench_samples (
                id INTEGER PRIMARY KEY, bench_id INT, ts TEXT, elapsed INT, pkg_w REAL, tctl REAL, ccd0_c REAL, ccd1_c REAL,
                eff_avg REAL, v_avg REAL, v_max REAL, vid_avg REAL, core_temp_max REAL,
                fan_cpu INT, fan_gpu INT, fan_pch INT, ec_cpu_c INT, ec_gpu_c INT, ec_pch_c INT,
                ac INT, bat_w REAL, bat_pct REAL, bat_wh REAL, per_core TEXT);
            """);
        Migrate();
        Exec("""
            CREATE INDEX IF NOT EXISTS ix_runs_campaign ON runs(campaign_id, core, margin);
            CREATE INDEX IF NOT EXISTS ix_samples_run ON samples(run_id);
            CREATE INDEX IF NOT EXISTS ix_ticks_ts ON ticks(ts);
            CREATE INDEX IF NOT EXISTS ix_ticks_session ON ticks(session_id);
            CREATE INDEX IF NOT EXISTS ix_events_ts ON events(ts);
            CREATE INDEX IF NOT EXISTS ix_bench_samples ON bench_samples(bench_id);
            """);
        SetMeta("schema_version", SchemaVersion.ToString());
    }

    /// <summary>
    /// Columns added after a table first shipped, one ALTER each, ignored
    /// when already there. A database from 0.1/0.2 (one per directory, no
    /// campaign ids) gets the columns too, so `db import` can read it if it
    /// ever has to; the JSONL next to it is what the import actually uses.
    /// </summary>
    private void Migrate()
    {
        (string Table, string Column)[] added =
        [
            ("runs", "campaign_id INT"), ("runs", "stage TEXT"), ("runs", "samples INT"), ("runs", "volt_max REAL"),
            ("runs", "temp_max REAL"), ("runs", "clock_eff_p10 REAL"), ("runs", "boot TEXT"),
            ("samples", "campaign_id INT"), ("samples", "run_id INT"), ("samples", "stage TEXT"), ("samples", "tctl REAL"),
            ("ticks", "session_id INT"), ("ticks", "ac INT"), ("ticks", "bat_w REAL"), ("ticks", "bat_pct REAL"), ("ticks", "bat_wh REAL"),
            ("ticks", "bat_full_wh REAL"), ("ticks", "ec_cpu_c INT"), ("ticks", "ec_gpu_c INT"), ("ticks", "ec_pch_c INT"),
            ("ticks", "fan_cpu INT"), ("ticks", "fan_gpu INT"), ("ticks", "fan_pch INT"), ("ticks", "power_mode INT"),
            ("ticks", "gpu_mode INT"), ("ticks", "hz INT"), ("ticks", "brightness INT"),
            // 0.4.0
            ("ticks", "core_temp_max REAL"), ("ticks", "core_hot INT"), ("ticks", "core_volt_mean REAL"), ("ticks", "core_ghz_max REAL"),
            ("ticks", "idle_s INT"), ("ticks", "charge_w REAL"), ("ticks", "charge_mode TEXT"), ("ticks", "dgpu INT"), ("ticks", "overlay TEXT"), ("ticks", "smu_ms INT"),
            ("events", "source TEXT"), ("events", "session_id INT"), ("events", "campaign_id INT"),
        ];
        foreach (var (table, column) in added)
            try { Exec($"ALTER TABLE {table} ADD COLUMN {column}"); } catch (SqliteException) { /* already there */ }
    }

    // ---- meta ----

    public string? Meta(string key)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string value)
        => Exec("INSERT INTO meta (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value", ("$k", key), ("$v", value));

    // ---- campaigns, runs, samples, limits ----

    /// <summary>The campaign by name; created on first use, resumed after.</summary>
    public long OpenCampaign(string name, string dir, object plan, IEnumerable<int> cores, bool quick)
    {
        if (CampaignId(name) is { } id) return id;
        Exec("INSERT INTO campaigns (name, dir, started, plan, cores, quick) VALUES ($n, $d, $s, $p, $c, $q)",
            ("$n", name), ("$d", dir), ("$s", Iso(DateTime.Now)), ("$p", JsonSerializer.Serialize(plan)), ("$c", string.Join(",", cores)), ("$q", quick ? 1 : 0));
        return LastId();
    }

    public void EndCampaign(long id) => Exec("UPDATE campaigns SET ended = $e WHERE id = $id AND ended IS NULL", ("$e", Iso(DateTime.Now)), ("$id", id));

    public long? CampaignId(string name)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id FROM campaigns WHERE name = $n";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() is long id ? id : null;
    }

    public List<CampaignRow> Campaigns()
        => Read("SELECT id, name, dir, started, ended, plan, cores, quick FROM campaigns ORDER BY started",
            r => new CampaignRow(r.GetInt64(0), r.GetString(1), S(r, 2), Ts(r, 3)!.Value, Ts(r, 4), S(r, 5), S(r, 6) ?? "", !r.IsDBNull(7) && r.GetInt32(7) != 0));

    /// <summary>A run starts as `running`; <see cref="EndRun"/> fills the verdict. A row still `running` at the next start is a hang or a killed process.</summary>
    public long BeginRun(long campaignId, int core, int margin, string engine, string stage, DateTime started, DateTime? boot)
    {
        Exec("INSERT INTO runs (campaign_id, core, margin, engine, stage, verdict, started, boot) VALUES ($c, $core, $m, $e, $s, 'running', $st, $b)",
            ("$c", campaignId), ("$core", core), ("$m", margin), ("$e", engine), ("$s", stage), ("$st", Iso(started)), ("$b", boot is { } b ? Iso(b) : null));
        return LastId();
    }

    public void EndRun(long id, RunResult r)
    {
        var t = r.Telemetry;
        Exec("""
            UPDATE runs SET verdict = $verdict, seconds = $seconds, error = $error, exit_code = $exit, whea = $whea, lines = $lines,
                suspensions = $susp, samples = $n, volt = $volt, volt_max = $vmax, ghz = $ghz, watts = $watts, temp = $temp, temp_max = $tmax,
                pkg_w = $pkg, clock = $clock, clock_eff = $eff, clock_eff_p10 = $p10, ended = $ended, stage = $stage
            WHERE id = $id
            """,
            ("$id", id), ("$verdict", r.Verdict), ("$seconds", r.Seconds), ("$error", r.Error), ("$exit", r.ExitCode), ("$whea", r.Whea),
            ("$lines", r.Lines), ("$susp", r.Suspensions), ("$n", t?.Samples), ("$volt", t?.VoltMedian), ("$vmax", t?.VoltMax), ("$ghz", t?.FreqMedian),
            ("$watts", t?.PowerMedian), ("$temp", t?.TempMedian), ("$tmax", t?.TempMax), ("$pkg", t?.PackagePowerMedian), ("$clock", t?.ClockMedian),
            ("$eff", t?.ClockEffectiveMedian), ("$p10", t?.ClockEffectiveP10), ("$ended", Iso(r.Ended)), ("$stage", r.Stage));
    }

    /// <summary>A whole run in one go (the import, and a run that never started its engine).</summary>
    public long AddRun(long campaignId, RunResult r, DateTime? boot = null)
    {
        var id = BeginRun(campaignId, r.Core, r.Margin, r.Engine, r.Stage, r.Started, boot);
        EndRun(id, r);
        return id;
    }

    public RunningRun? RunningRun(long campaignId)
        => Read("SELECT id, core, margin, engine, stage, started, boot FROM runs WHERE campaign_id = $c AND verdict = 'running' ORDER BY id DESC LIMIT 1",
            r => new RunningRun(r.GetInt64(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), S(r, 4) ?? "sweep", Ts(r, 5)!.Value, Ts(r, 6)),
            ("$c", campaignId)).FirstOrDefault();

    public void AddSample(long campaignId, long runId, int core, int margin, string engine, string stage, Sample s)
        => Exec("""
            INSERT INTO samples (campaign_id, run_id, core, margin, engine, stage, ts, elapsed, clock, clock_eff, volt, ghz, watts, temp, pkg_w, tctl)
            VALUES ($c, $r, $core, $margin, $engine, $stage, $ts, $el, $clock, $eff, $volt, $ghz, $watts, $temp, $pkg, $tctl)
            """,
            ("$c", campaignId), ("$r", runId), ("$core", core), ("$margin", margin), ("$engine", engine), ("$stage", stage), ("$ts", Iso(s.Ts)), ("$el", s.Elapsed),
            ("$clock", s.Clock), ("$eff", s.ClockEffective), ("$volt", s.Volt), ("$ghz", s.Freq), ("$watts", s.Power),
            ("$temp", s.Temp), ("$pkg", s.PackagePower), ("$tctl", s.Tctl));

    public List<RunResult> Runs(long campaignId)
        => Read("""
            SELECT core, margin, engine, verdict, seconds, error, exit_code, whea, lines, suspensions,
                   volt, ghz, watts, temp, pkg_w, clock, clock_eff, started, ended, stage, samples, volt_max, temp_max, clock_eff_p10
            FROM runs WHERE campaign_id = $c AND verdict <> 'running' ORDER BY id
            """, r =>
            {
                var tele = r.IsDBNull(10) && r.IsDBNull(11) ? null
                    : new SampleSummary(r.IsDBNull(20) ? 0 : r.GetInt32(20), D(r, 15), D(r, 16), D(r, 23), D(r, 10), D(r, 21), D(r, 11), D(r, 12), D(r, 14), D(r, 13), D(r, 22));
                return new RunResult(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    S(r, 5), r.IsDBNull(6) ? null : r.GetInt32(6), r.IsDBNull(7) ? 0 : r.GetInt32(7), r.IsDBNull(8) ? 0 : r.GetInt32(8), r.IsDBNull(9) ? 0 : r.GetInt32(9),
                    tele, Ts(r, 17)!.Value, Ts(r, 18) ?? Ts(r, 17)!.Value, S(r, 19) ?? "sweep");
            }, ("$c", campaignId));

    public void SetLimit(long campaignId, int core, int? margin)
        => Exec("INSERT INTO limits (campaign_id, core, margin, ts) VALUES ($c, $core, $m, $ts) ON CONFLICT(campaign_id, core) DO UPDATE SET margin = excluded.margin, ts = excluded.ts",
            ("$c", campaignId), ("$core", core), ("$m", margin), ("$ts", Iso(DateTime.Now)));

    /// <summary>Core -> limit (null: no margin up to the top survived). Cores not swept are absent.</summary>
    public Dictionary<int, int?> Limits(long campaignId)
        => Read("SELECT core, margin FROM limits WHERE campaign_id = $c ORDER BY core", r => (Core: r.GetInt32(0), Margin: r.IsDBNull(1) ? (int?)null : r.GetInt32(1)), ("$c", campaignId))
            .ToDictionary(x => x.Core, x => x.Margin);

    /// <summary>Every limit of every campaign, oldest campaign first: the history of the silicon.</summary>
    public List<LimitRow> AllLimits()
        => Read("SELECT l.campaign_id, c.name, l.core, l.margin, l.ts FROM limits l JOIN campaigns c ON c.id = l.campaign_id ORDER BY c.started, l.core",
            r => new LimitRow(r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetInt32(3), Ts(r, 4)!.Value));

    // ---- guard: sessions, ticks, events, health ----

    public long BeginSession(int pid, string profile, int intervalSeconds, bool adhoc, DateTime? started = null)
    {
        Exec("INSERT INTO sessions (started, pid, profile, interval, adhoc) VALUES ($s, $pid, $p, $i, $a)",
            ("$s", Iso(started ?? DateTime.Now)), ("$pid", pid), ("$p", profile), ("$i", intervalSeconds), ("$a", adhoc ? 1 : 0));
        return LastId();
    }

    public void EndSession(long id, int exitCode, DateTime? ended = null)
        => Exec("UPDATE sessions SET ended = $e, exit_code = $c WHERE id = $id", ("$e", Iso(ended ?? DateTime.Now)), ("$c", exitCode), ("$id", id));

    public List<SessionRow> Sessions(DateTime? since = null)
        => Read("SELECT id, started, ended, pid, profile, interval, adhoc, exit_code FROM sessions WHERE $since IS NULL OR started >= $since OR ended >= $since OR ended IS NULL ORDER BY id",
            r => new SessionRow(r.GetInt64(0), Ts(r, 1)!.Value, Ts(r, 2), r.IsDBNull(3) ? null : r.GetInt32(3), S(r, 4), r.IsDBNull(5) ? 60 : r.GetInt32(5),
                !r.IsDBNull(6) && r.GetInt32(6) != 0, r.IsDBNull(7) ? null : r.GetInt32(7)),
            ("$since", since is { } s ? Iso(s) : null));

    public void AddTick(long? sessionId, GuardTick t)
    {
        var x = t.Extras ?? TickExtras.Empty;
        Exec("""
            INSERT INTO ticks (session_id, ts, elapsed, ok, hardware, whea, cpu, pkg_w, state,
                ac, bat_w, bat_pct, bat_wh, bat_full_wh, ec_cpu_c, ec_gpu_c, ec_pch_c, fan_cpu, fan_gpu, fan_pch, power_mode, gpu_mode, hz, brightness,
                core_temp_max, core_hot, core_volt_mean, core_ghz_max, idle_s, charge_w, charge_mode, dgpu, overlay, smu_ms)
            VALUES ($sid, $ts, $el, $ok, $hw, $whea, $cpu, $pkg, $state,
                $ac, $batw, $batpct, $batwh, $batfull, $eccpu, $ecgpu, $ecpch, $fancpu, $fangpu, $fanpch, $pmode, $gmode, $hz, $bright,
                $ctmax, $chot, $cvolt, $cghz, $idle, $chargew, $chargemode, $dgpu, $overlay, $smu)
            """,
            ("$sid", sessionId), ("$ts", Iso(t.Ts)), ("$el", t.Elapsed), ("$ok", t.Ok ? 1 : 0), ("$hw", string.Join(",", t.Hardware.Select(h => h?.ToString() ?? "-"))),
            ("$whea", t.Whea), ("$cpu", t.CpuLoad), ("$pkg", t.PackagePower), ("$state", t.State),
            ("$ac", x.Ac is { } ac ? (ac ? 1 : 0) : null), ("$batw", x.BatW), ("$batpct", x.BatPct), ("$batwh", x.BatWh), ("$batfull", x.BatFullWh),
            ("$eccpu", x.EcCpuC), ("$ecgpu", x.EcGpuC), ("$ecpch", x.EcPchC), ("$fancpu", x.FanCpu), ("$fangpu", x.FanGpu), ("$fanpch", x.FanPch),
            ("$pmode", x.PowerMode), ("$gmode", x.GpuMode), ("$hz", x.Hz), ("$bright", x.Brightness),
            ("$ctmax", x.CoreTempMax), ("$chot", x.CoreHot), ("$cvolt", x.CoreVoltMean), ("$cghz", x.CoreGhzMax), ("$idle", x.IdleS),
            ("$chargew", x.ChargeW), ("$chargemode", x.ChargeMode), ("$dgpu", x.Dgpu is { } d ? (d ? 1 : 0) : null), ("$overlay", x.Overlay), ("$smu", x.SmuMs));
    }

    public List<TickRow> Ticks(DateTime since, DateTime? until = null)
        => Read("""
            SELECT id, session_id, ts, elapsed, ok, hardware, whea, cpu, pkg_w, state,
                   ac, bat_w, bat_pct, bat_wh, bat_full_wh, ec_cpu_c, ec_gpu_c, ec_pch_c, fan_cpu, fan_gpu, fan_pch, power_mode, gpu_mode, hz, brightness,
                   core_temp_max, core_hot, core_volt_mean, core_ghz_max, idle_s, charge_w, charge_mode, dgpu, overlay, smu_ms
            FROM ticks WHERE ts >= $since AND ($until IS NULL OR ts < $until) ORDER BY id
            """, r =>
            {
                var hw = (S(r, 5) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(h => int.TryParse(h, out var v) ? v : (int?)null).ToArray();
                var extras = new TickExtras(r.IsDBNull(10) ? null : r.GetInt32(10) != 0, D(r, 11), D(r, 12), D(r, 13), D(r, 14),
                    I(r, 15), I(r, 16), I(r, 17), I(r, 18), I(r, 19), I(r, 20), I(r, 21), I(r, 22), I(r, 23), I(r, 24),
                    D(r, 25), I(r, 26), D(r, 27), D(r, 28), I(r, 29), D(r, 30), S(r, 31), r.IsDBNull(32) ? null : r.GetInt32(32) != 0, S(r, 33), I(r, 34));
                return new TickRow(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1),
                    new GuardTick(Ts(r, 2)!.Value, r.IsDBNull(3) ? 0 : r.GetInt32(3), !r.IsDBNull(4) && r.GetInt32(4) != 0, hw, r.IsDBNull(6) ? 0 : r.GetInt32(6),
                        D(r, 7), D(r, 8), S(r, 9) ?? "", extras));
            }, ("$since", Iso(since)), ("$until", until is { } u ? Iso(u) : null));

    public void AddEvent(string source, long? sessionId, long? campaignId, DateTime ts, string kind, string detail)
        => Exec("INSERT INTO events (ts, source, session_id, campaign_id, kind, detail) VALUES ($ts, $src, $sid, $cid, $kind, $detail)",
            ("$ts", Iso(ts)), ("$src", source), ("$sid", sessionId), ("$cid", campaignId), ("$kind", kind), ("$detail", detail));

    /// <summary>Events of one source (guard or sweep); of one campaign when given; since a time when given.</summary>
    public List<(DateTime Ts, string Kind, string Detail)> Events(string source, long? campaignId = null, DateTime? since = null)
        => Read("SELECT ts, kind, detail FROM events WHERE source = $src AND ($cid IS NULL OR campaign_id = $cid) AND ($since IS NULL OR ts >= $since) ORDER BY id",
            r => (Ts(r, 0)!.Value, r.GetString(1), S(r, 2) ?? ""), ("$src", source), ("$cid", campaignId), ("$since", since is { } s ? Iso(s) : null));

    public void AddHealth(HealthSample s)
        => Exec("INSERT INTO health (ts, full_wh, design_wh, cycles) VALUES ($ts, $full, $design, $cycles)",
            ("$ts", Iso(s.Ts)), ("$full", s.FullWh), ("$design", s.DesignWh), ("$cycles", s.Cycles));

    public List<HealthSample> Health()
        => Read("SELECT ts, full_wh, design_wh, cycles FROM health ORDER BY ts", r => new HealthSample(Ts(r, 0)!.Value, D(r, 1), D(r, 2), I(r, 3)));

    public DateTime? LastHealthTs()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT MAX(ts) FROM health";
        return cmd.ExecuteScalar() is string s ? DateTime.Parse(s) : null;
    }

    // ---- bench (dev log) ----

    public long BeginBench(string name, int intervalSeconds)
    {
        Exec("INSERT INTO bench (name, started, interval) VALUES ($n, $s, $i)", ("$n", name), ("$s", Iso(DateTime.Now)), ("$i", intervalSeconds));
        return LastId();
    }

    public void EndBench(long id) => Exec("UPDATE bench SET ended = $e WHERE id = $id", ("$e", Iso(DateTime.Now)), ("$id", id));

    /// <summary>One `dev log` row. <paramref name="perCore"/>: whatever is per core, as JSON (effective clocks and voltages).</summary>
    public void AddBenchSample(long benchId, DateTime ts, int elapsed, double? pkgW, double? tctl, double? ccd0, double? ccd1, double? effAvg,
        double? vAvg, double? vMax, double? vidAvg, double? coreTempMax, int? fanCpu, int? fanGpu, int? fanPch, int? ecCpu, int? ecGpu, int? ecPch,
        bool? ac, double? batW, double? batPct, double? batWh, string? perCore)
        => Exec("""
            INSERT INTO bench_samples (bench_id, ts, elapsed, pkg_w, tctl, ccd0_c, ccd1_c, eff_avg, v_avg, v_max, vid_avg, core_temp_max,
                fan_cpu, fan_gpu, fan_pch, ec_cpu_c, ec_gpu_c, ec_pch_c, ac, bat_w, bat_pct, bat_wh, per_core)
            VALUES ($b, $ts, $el, $pkg, $tctl, $ccd0, $ccd1, $eff, $vavg, $vmax, $vid, $ctmax,
                $fcpu, $fgpu, $fpch, $ecpu, $egpu, $epch, $ac, $batw, $batpct, $batwh, $pc)
            """,
            ("$b", benchId), ("$ts", Iso(ts)), ("$el", elapsed), ("$pkg", pkgW), ("$tctl", tctl), ("$ccd0", ccd0), ("$ccd1", ccd1), ("$eff", effAvg),
            ("$vavg", vAvg), ("$vmax", vMax), ("$vid", vidAvg), ("$ctmax", coreTempMax), ("$fcpu", fanCpu), ("$fgpu", fanGpu), ("$fpch", fanPch),
            ("$ecpu", ecCpu), ("$egpu", ecGpu), ("$epch", ecPch), ("$ac", ac is { } a ? (a ? 1 : 0) : null), ("$batw", batW), ("$batpct", batPct), ("$batwh", batWh), ("$pc", perCore));

    // ---- ad hoc: query, stats, export ----

    /// <summary>A read-only statement (`db sql`). Anything that writes fails with "attempt to write a readonly database".</summary>
    public (string[] Columns, List<object?[]> Rows) Query(string sql)
    {
        Exec("PRAGMA query_only=1");
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            var cols = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToArray();
            var rows = new List<object?[]>();
            while (r.Read())
            {
                var row = new object?[r.FieldCount];
                for (var i = 0; i < r.FieldCount; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
                rows.Add(row);
            }
            return (cols, rows);
        }
        finally { Exec("PRAGMA query_only=0"); }
    }

    /// <summary>Rows per table and the file size (with the WAL).</summary>
    public (List<(string Table, long Rows)> Counts, long Bytes) Stats()
    {
        var counts = new List<(string, long)>();
        foreach (var t in Tables)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {t}";
            counts.Add((t, (long)cmd.ExecuteScalar()!));
        }
        long bytes = 0;
        foreach (var p in new[] { Path, Path + "-wal" })
            if (File.Exists(p)) bytes += new FileInfo(p).Length;
        return (counts, bytes);
    }

    /// <summary>Every row of a table, optionally since a time (tables with `ts`; `started` for campaigns, sessions and bench).</summary>
    public (string[] Columns, List<object?[]> Rows) Export(string table, DateTime? since)
    {
        if (!Tables.Contains(table)) throw new ArgumentException($"unknown table {table}; one of {string.Join(", ", Tables)}");
        var tsColumn = table is "campaigns" or "sessions" or "bench" ? "started" : "ts";
        var where = since is { } s && table != "limits" ? $" WHERE {tsColumn} >= '{Iso(s)}'" : "";
        return Query($"SELECT * FROM {table}{where} ORDER BY rowid");
    }

    // ---- the import of the JSONL era (0.1 / 0.2) ----

    /// <summary>
    /// Reads `guard\guard.jsonl` and every `campaigns\*\` (campaign.json,
    /// runs.jsonl, samples.jsonl, limits.json) from a data directory into
    /// this database, once each (`meta imported:...`). Deletes nothing.
    /// Returns one line per thing imported or skipped.
    /// </summary>
    public List<string> ImportLegacy(string dataDir)
    {
        var report = new List<string>();
        var guard = System.IO.Path.Combine(dataDir, "guard", "guard.jsonl");
        if (File.Exists(guard)) report.Add(ImportGuardJournal(guard));

        var campaigns = System.IO.Path.Combine(dataDir, "campaigns");
        if (Directory.Exists(campaigns))
            foreach (var dir in Directory.GetDirectories(campaigns).OrderBy(d => d, StringComparer.Ordinal))
                if (File.Exists(System.IO.Path.Combine(dir, "runs.jsonl")) || File.Exists(System.IO.Path.Combine(dir, "limits.json")))
                    report.Add(ImportCampaignDir(dir));
        if (report.Count == 0) report.Add("nothing to import");
        return report;
    }

    /// <summary>
    /// Incremental: the 0.2 guard keeps appending until it is reinstalled, so
    /// the meta row remembers how many lines are in and which session was
    /// open, and a later import continues from there.
    /// </summary>
    private string ImportGuardJournal(string path)
    {
        var key = "imported:" + path;
        long done = 0; long? session = null;
        if (Meta(key) is { } state)
            foreach (var part in state.Split(';'))
            {
                if (part.StartsWith("lines=") && long.TryParse(part[6..], out var n)) done = n;
                if (part.StartsWith("session=") && long.TryParse(part[8..], out var s)) session = s;
            }
        int ticks = 0, events = 0, health = 0, sessions = 0;
        long seen = 0;
        using (var tx = _db.BeginTransaction())
        {
            foreach (var line in Lines(path))
            {
                if (++seen <= done) continue;
                using var doc = JsonDocument.Parse(line);
                var e = doc.RootElement;
                var kind = e.GetProperty("kind").GetString()!;
                if (kind == "tick")
                {
                    AddTick(session, new GuardTick(e.GetProperty("Ts").GetDateTime(), e.GetProperty("Elapsed").GetInt32(), e.GetProperty("Ok").GetBoolean(),
                        e.GetProperty("Hardware").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetInt32() : (int?)null).ToArray(),
                        e.GetProperty("Whea").GetInt32(), Num(e, "CpuLoad"), Num(e, "PackagePower"), e.GetProperty("State").GetString()!));
                    ticks++;
                    continue;
                }
                var ts = e.GetProperty("ts").GetDateTime();
                if (kind == "health")
                {
                    AddHealth(new HealthSample(ts, Num(e, "fullWh"), Num(e, "designWh"),
                        e.TryGetProperty("cycles", out var cy) && cy.ValueKind == JsonValueKind.Number ? cy.GetInt32() : null));
                    health++;
                    continue;
                }
                var detail = e.GetProperty("detail").GetString() ?? "";
                if (kind == "start")
                {
                    // "profile -35,-35,...  interval 60s  no time limit"
                    var profile = detail.StartsWith("profile ") ? detail[8..].Split("  ")[0] : "";
                    var interval = 60;
                    var i = detail.IndexOf("interval ", StringComparison.Ordinal);
                    if (i >= 0) int.TryParse(detail[(i + 9)..].TrimEnd().Split('s')[0], out interval);
                    session = BeginSession(0, profile, interval, false, ts);
                    sessions++;
                }
                AddEvent("guard", session, null, ts, kind, detail);
                events++;
                if (kind == "restore" && session is { } s)
                {
                    var code = detail.LastIndexOf("code ", StringComparison.Ordinal) is var c and >= 0 && int.TryParse(detail[(c + 5)..].Trim(), out var v) ? v : 0;
                    EndSession(s, code, ts);
                    session = null;
                }
            }
            SetMeta(key, $"lines={seen};session={session?.ToString() ?? ""}");
            tx.Commit();
        }
        return seen == done ? $"{path}: already up to date ({done} lines)"
            : $"{path}: {sessions} sessions, {ticks} ticks, {events} events, {health} health samples{(done > 0 ? $" (lines {done + 1}-{seen})" : "")}";
    }

    private string ImportCampaignDir(string dir)
    {
        var key = "imported:" + dir;
        if (Meta(key) is not null) return $"{dir}: already imported";
        var name = System.IO.Path.GetFileName(dir.TrimEnd('\\', '/'));
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        int runs = 0, samples = 0, limits = 0;
        using (var tx = _db.BeginTransaction())
        {
            // campaign.json: started, plan, cores. Written once by the old Sweep; absent on the very first campaigns.
            DateTime started = DateTime.MaxValue; string? plan = null; var cores = "";
            var cj = System.IO.Path.Combine(dir, "campaign.json");
            if (File.Exists(cj))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(cj));
                var e = doc.RootElement;
                if (e.TryGetProperty("started", out var st)) started = st.GetDateTime();
                if (e.TryGetProperty("plan", out var pl)) plan = pl.GetRawText();
                if (e.TryGetProperty("cores", out var co) && co.ValueKind == JsonValueKind.Array) cores = string.Join(",", co.EnumerateArray().Select(x => x.GetInt32()));
            }

            var runList = new List<(long Id, RunResult Run)>();
            var runsPath = System.IO.Path.Combine(dir, "runs.jsonl");
            if (File.Exists(runsPath))
                foreach (var line in Lines(runsPath))
                {
                    var r = JsonSerializer.Deserialize<RunResult>(line, opts)!;
                    if (r.Started < started) started = r.Started;
                    runList.Add((0, r));
                }
            if (started == DateTime.MaxValue) started = Directory.GetCreationTime(dir);

            Exec("INSERT INTO campaigns (name, dir, started, plan, cores, quick) VALUES ($n, $d, $s, $p, $c, 0)",
                ("$n", name), ("$d", dir), ("$s", Iso(started)), ("$p", plan), ("$c", cores));
            var campaignId = LastId();
            for (var i = 0; i < runList.Count; i++)
            {
                runList[i] = (AddRun(campaignId, runList[i].Run), runList[i].Run);
                runs++;
            }
            if (runList.Count > 0)
                Exec("UPDATE campaigns SET ended = $e WHERE id = $id", ("$e", Iso(runList.Max(x => x.Run.Ended))), ("$id", campaignId));

            // A sample belongs to the run of its core/margin/engine whose window holds its timestamp.
            var byKey = runList.GroupBy(x => (x.Run.Core, x.Run.Margin, x.Run.Engine)).ToDictionary(g => g.Key, g => g.ToList());
            var samplesPath = System.IO.Path.Combine(dir, "samples.jsonl");
            if (File.Exists(samplesPath))
                foreach (var line in Lines(samplesPath))
                {
                    using var doc = JsonDocument.Parse(line);
                    var e = doc.RootElement;
                    var core = e.GetProperty("core").GetInt32();
                    var margin = e.GetProperty("margin").GetInt32();
                    var engine = e.GetProperty("engine").GetString()!;
                    var ts = e.GetProperty("Ts").GetDateTime();
                    var run = byKey.TryGetValue((core, margin, engine), out var candidates)
                        ? candidates.FirstOrDefault(x => x.Run.Started.AddSeconds(-2) <= ts && ts <= x.Run.Ended.AddSeconds(2))
                        : default;
                    var stage = e.TryGetProperty("stage", out var sg) && sg.ValueKind == JsonValueKind.String ? sg.GetString()! : run.Run?.Stage ?? "sweep";
                    AddSample(campaignId, run.Id, core, margin, engine, stage,
                        new Sample(ts, e.GetProperty("Elapsed").GetInt32(), Num(e, "Clock"), Num(e, "ClockEffective"), Num(e, "Volt"), Num(e, "Freq"),
                            Num(e, "Power"), Num(e, "Temp"), Num(e, "PackagePower"), Num(e, "Tctl")));
                    samples++;
                }

            var limitsPath = System.IO.Path.Combine(dir, "limits.json");
            if (Journal.ReadJsonFile<Dictionary<string, int?>>(limitsPath) is { } lim)
                foreach (var (k, v) in lim)
                {
                    var closed = runList.Where(x => x.Run.Core == int.Parse(k)).Select(x => x.Run.Ended).DefaultIfEmpty(started).Max();
                    Exec("INSERT OR REPLACE INTO limits (campaign_id, core, margin, ts) VALUES ($c, $core, $m, $ts)",
                        ("$c", campaignId), ("$core", int.Parse(k)), ("$m", v), ("$ts", Iso(closed)));
                    limits++;
                }
            SetMeta(key, Iso(DateTime.Now));
            tx.Commit();
        }
        return $"{dir}: campaign {name}, {runs} runs, {samples} samples, {limits} limits";
    }

    /// <summary>Like File.ReadLines but shares with a live writer.</summary>
    private static IEnumerable<string> Lines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        while (sr.ReadLine() is { } line)
            if (line.Trim().Length > 0) yield return line;
    }

    // ---- plumbing ----

    private static string Iso(DateTime t) => t.ToString("o");

    private static double? Num(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;

    private static double? D(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
    private static int? I(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
    private static string? S(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static DateTime? Ts(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : DateTime.Parse(r.GetString(i));

    private long LastId()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT last_insert_rowid()";
        return (long)cmd.ExecuteScalar()!;
    }

    private List<T> Read<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] args)
    {
        var list = new List<T>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    private void Exec(string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
