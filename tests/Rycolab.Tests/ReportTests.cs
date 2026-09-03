using Rycolab.Core;
using Rycolab.Core.Legion;

namespace Rycolab.Tests;

public class PowerReportTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 10, 0, 0);

    private static TickRow Tick(int minute, bool ac, double? pkg, double? batW = null, double? batWh = null, double? pct = null, int? mode = null, int? gpu = null, int? hz = null, int? ecCpu = null,
        double? coreTemp = null, int? coreHot = null, double? ghz = null, int? idle = null, double? chargeW = null, string? chargeMode = null, bool? dgpu = null, string? overlay = null, int? smu = null)
        => new(minute, 1, new GuardTick(T0.AddMinutes(minute), minute * 60, true, [-40], 0, 5.0, pkg, "ok",
            new TickExtras(ac, batW, pct, batWh, 80.0, ecCpu, null, null, 2000, null, null, mode, gpu, hz, 40,
                coreTemp, coreHot, null, ghz, idle, chargeW, chargeMode, dgpu, overlay, smu)));

    private static readonly List<SessionRow> Sessions = [new(1, T0, null, 1, "-40", 60, false, null)];

    [Fact]
    public void PeriodsParse()
    {
        var now = new DateTime(2026, 9, 3, 12, 0, 0);
        var d = PowerReport.Period(null, null, now)!.Value;
        Assert.Equal(now.AddDays(-30), d.Since);
        Assert.Equal("last 30 days", d.Label);
        Assert.Equal(now.AddHours(-24), PowerReport.Period("24h", null, now)!.Value.Since);
        Assert.Equal(now.AddDays(-14), PowerReport.Period("2w", null, now)!.Value.Since);
        var m = PowerReport.Period(null, "2026-08", now)!.Value;
        Assert.Equal(new DateTime(2026, 8, 1), m.Since);
        Assert.Equal(new DateTime(2026, 9, 1), m.Until);
        Assert.Equal("August 2026", m.Label);
        Assert.Null(PowerReport.Period("soon", null, now));
        Assert.Null(PowerReport.Period("0d", null, now));
        Assert.Null(PowerReport.Period(null, "august", now));
    }

    [Fact]
    public void BatterySessionsSplitOnAcAndOnHoles()
    {
        List<TickRow> ticks =
        [
            Tick(0, true, 20),
            Tick(1, false, 8, batW: 10, batWh: 60, pct: 75, mode: 1, gpu: 1, hz: 60),
            Tick(2, false, 9, batW: 12, batWh: 59.8, pct: 74.8, mode: 1, gpu: 1, hz: 60),
            Tick(3, false, 7, batW: 11, batWh: 59.6, pct: 74.5, mode: 1, gpu: 1, hz: 60),
            Tick(4, true, 25),
            Tick(5, false, 6, batW: 9, batWh: 59, pct: 74),
            Tick(15, false, 6, batW: 9, batWh: 57, pct: 71),   // a 10 min hole: the machine slept -> a new session
        ];
        var sessions = PowerReport.BatterySessions(ticks, _ => 1 / 60.0);
        Assert.Equal(3, sessions.Count);
        var s = sessions[0];
        Assert.Equal(T0.AddMinutes(1), s.Start);
        Assert.Equal(T0.AddMinutes(3), s.End);
        Assert.Equal(3 / 60.0, s.Hours, 6);
        Assert.Equal(0.4, s.WhUsed!.Value, 6);
        Assert.Equal(11.0, s.MeanW!.Value, 6);
        Assert.Equal(75, s.PctStart);
        Assert.Equal(74.5, s.PctEnd);
        Assert.Equal(1, s.PowerMode);
        Assert.Equal(60, s.Hz);
        Assert.Null(sessions[1].WhUsed);   // one tick: no delta
    }

    [Fact]
    public void BuildSummarisesAcAndBattery()
    {
        List<TickRow> ticks =
        [
            Tick(0, true, 10, mode: 3, ecCpu: 50, coreTemp: 60, coreHot: 3, ghz: 5.2, idle: 10, chargeW: 45, chargeMode: "conservation", dgpu: true, overlay: "balanced", smu: 30),
            Tick(1, true, 20, mode: 3, ecCpu: 60, coreTemp: 70, coreHot: 3, ghz: 5.4, idle: 400, chargeW: 45, chargeMode: "conservation", dgpu: true, overlay: "balanced", smu: 40),
            Tick(2, true, 30, mode: 3, ecCpu: 70, coreTemp: 80, coreHot: 5, ghz: 5.45, idle: 5, chargeMode: "conservation", dgpu: true, overlay: "balanced", smu: 50),
            Tick(3, false, 8, batW: 10, batWh: 60, pct: 75, mode: 1, gpu: 1, hz: 60, ecCpu: 45, coreTemp: 50, coreHot: 0, ghz: 4.0, idle: 20, chargeMode: "conservation", dgpu: false, overlay: "best power efficiency", smu: 35),
            Tick(4, false, 6, batW: 12, batWh: 59.8, pct: 74.8, mode: 1, gpu: 1, hz: 60, ecCpu: 41, coreTemp: 52, coreHot: 0, ghz: 4.4, idle: 900, chargeMode: "conservation", dgpu: false, overlay: "best power efficiency", smu: 45),
        ];
        var events = new List<(DateTime, string, string)> { (T0, "start", "x"), (T0.AddMinutes(1), "changed", "y"), (T0.AddMinutes(2), "resume", "z") };
        var health = new List<HealthSample> { new(T0, 80.5, 99.9, 12), new(T0.AddMinutes(4), 80.1, 99.9, 13) };
        var md = PowerReport.Build("last 30 days", T0.AddDays(-30), T0.AddDays(1), ticks, Sessions, events, health);

        Assert.Contains("Guarded 0.1 h in 1 guard session; on battery 0.0 h (40 %) in 1 battery session.", md);
        Assert.Contains("| package W mean / p50 / p95 | 20.0 / 20.0 / 30.0 | 7.0 / 6.0 / 8.0 |", md);
        Assert.Contains("| EC CPU C mean / max | 60 / 70 | 43 / 45 |", md);
        Assert.Contains("| panel Hz / brightness (most of the time) | - / 40 | 60 / 40 |", md);
        Assert.Contains("| 2026-09-01 10:03 | 0.0 | 0.2 | 11.0 | 75 -> 75 | quiet | igpu-only | 60 | 40 | 50 | 0.0 |", md);
        Assert.Contains("### By power mode", md);
        Assert.Contains($"| {LenovoEc.ModeName(3)} | 0.1 | 0.0 | 20.0 | 60 |", md);
        Assert.Contains("| quiet | 0.0 | 0.0 | 7.0 | 43 |", md);
        Assert.Contains("| core temp max mean / max | 70 / 80 | 51 / 52 |", md);
        Assert.Contains("| hottest core (most of the time) | 3 | 0 |", md);
        Assert.Contains("| core GHz max p95 | 5.45 | 4.40 |", md);
        Assert.Contains("| battery W mean in use / idle | - | 10.0 / 12.0 |", md);
        Assert.Contains("| SMU read ms mean / p95 / max | 40 / 50 / 50 | 40 / 45 / 45 |", md);
        Assert.Contains("### By Windows overlay", md);
        Assert.Contains("| best power efficiency | 0.0 | 0.0 | 7.0 | 43 |", md);
        Assert.Contains("Charging: 0.0 h at 45.0 W mean; charge mode: conservation 0.1 h.", md);
        Assert.Contains("dGPU on the bus 0.0 h of the 0.0 h on battery; battery W mean - with it, 11.0 without.", md);
        Assert.Contains("Battery health: 80.1 Wh full charge (80.2 % of 99.9 Wh design), 13 cycles on 2026-09-01; -0.4 Wh since 2026-09-01 (2 samples).", md);
        Assert.Contains("Events: 0 WHEA, 0 resets, 1 margin lost, 1 resumes, 0 failed applies, 0 tick failures.", md);
    }

    [Fact]
    public void BuildWithoutTicksSaysSo()
        => Assert.Contains("No guard ticks in the period.", PowerReport.Build("last 7 days", T0, T0.AddDays(7), [], [], [], []));
}

public class CampaignsReportTests
{
    [Fact]
    public void LimitsSideBySide()
    {
        var t = new DateTime(2026, 8, 28, 12, 32, 0);
        List<CampaignRow> campaigns = [new(1, "find-a", null, t, t.AddHours(5), null, "0,1", false), new(2, "find-b", null, t.AddDays(3), null, null, "0", true), new(3, "empty", null, t.AddDays(4), null, null, "", false)];
        List<LimitRow> limits = [new(1, "find-a", 0, -40, t), new(1, "find-a", 1, null, t), new(2, "find-b", 0, -35, t)];
        var md = CampaignsReport.Build(campaigns, limits);
        Assert.Contains("| find-a | 2026-08-28 12:32 | 2026-08-28 17:32 | 0,1 | 2 |", md);
        Assert.Contains("| find-b (quick) | 2026-08-31 12:32 | unfinished | 0 | 1 |", md);
        Assert.Contains("| Core | find-a | find-b (quick) |", md);
        Assert.Contains("| 0 | -40 | -35 |", md);
        Assert.Contains("| 1 | none | - |", md);
        Assert.DoesNotContain("| empty |", md.Split("### Limit per core")[1]);
    }

    [Fact]
    public void NoCampaigns() => Assert.Contains("No campaigns in the database", CampaignsReport.Build([], []));
}

public class GuardReportTests
{
    [Fact]
    public void SessionsAndEvents()
    {
        var t = new DateTime(2026, 9, 1, 10, 0, 0);
        List<SessionRow> sessions = [new(1, t, t.AddHours(2), 10, "-40,-40", 60, false, 0), new(2, t.AddHours(3), null, 11, "-40,-40", 60, true, null)];
        var md = GuardReport.Build(sessions, [(t, "start", "profile -40,-40"), (t.AddMinutes(1), "tick", "no"), (t.AddHours(2), "restore", "a | b")]);
        Assert.Contains("| 2026-09-01 10:00 | 2026-09-01 12:00 | 2.0 | -40,-40 | ok |", md);
        Assert.Contains("| 2026-09-01 13:00 | running |", md);
        Assert.Contains("(ad hoc)", md);
        Assert.Contains("### Events (last 2)", md);
        Assert.Contains("`restore` a \\| b", md);
        Assert.DoesNotContain("`tick`", md);
    }
}
