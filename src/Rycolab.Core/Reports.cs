using System.Text;
using Rycolab.Core.Legion;

namespace Rycolab.Core;

/// <summary>
/// `rycolab report --power`: what the guard's ticks say about a period -
/// hours guarded and on battery, the battery sessions with their Wh, package
/// power on AC and on battery, the split by power and GPU mode, the EC's
/// temperatures and fans, the panel on battery, the pack's health, the
/// events. Pure functions over what <see cref="Store"/> returns.
/// </summary>
public static class PowerReport
{
    public sealed record BatterySession(DateTime Start, DateTime End, double Hours, double? WhUsed, double? MeanW, double? PctStart, double? PctEnd,
        int? PowerMode, int? GpuMode, int? Hz, int? Brightness, double? InUsePct = null, double DgpuHours = 0);

    /// <summary>A tick counts as "in use" when the user touched the machine within these seconds.</summary>
    public const int InUseSeconds = 300;

    private static bool? InUse(TickRow t) => t.Tick.Extras?.IdleS is { } s ? s < InUseSeconds : null;

    /// <summary>`--since 30d|7d|24h|12h` or `--month 2026-08`; default the last 30 days. Null when the text is not a period.</summary>
    public static (DateTime Since, DateTime Until, string Label)? Period(string? since, string? month, DateTime now)
    {
        if (month is not null)
        {
            if (!DateTime.TryParseExact(month, "yyyy-MM", null, System.Globalization.DateTimeStyles.None, out var m)) return null;
            return (m, m.AddMonths(1), m.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture));
        }
        since ??= "30d";
        if (since.Length < 2 || !int.TryParse(since[..^1], out var n) || n <= 0) return null;
        return since[^1] switch
        {
            'd' => (now.AddDays(-n), now, $"last {n} days"),
            'h' => (now.AddHours(-n), now, $"last {n} hours"),
            'w' => (now.AddDays(-7 * n), now, $"last {n} weeks"),
            _ => null,
        };
    }

    public static string Build(string label, DateTime since, DateTime until, List<TickRow> ticks, List<SessionRow> sessions,
        List<(DateTime Ts, string Kind, string Detail)> events, List<HealthSample> health)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Power, {label} ({since:yyyy-MM-dd} - {until:yyyy-MM-dd})");
        sb.AppendLine();
        if (ticks.Count == 0)
        {
            sb.AppendLine("No guard ticks in the period.");
            return sb.ToString();
        }

        var interval = sessions.ToDictionary(s => s.Id, s => s.Interval);
        double Hours(TickRow t) => (t.SessionId is { } id && interval.TryGetValue(id, out var i) ? i : 60) / 3600.0;
        var ac = ticks.Where(t => t.Tick.Extras?.Ac == true).ToList();
        var bat = ticks.Where(t => t.Tick.Extras?.Ac == false).ToList();
        var guardedH = ticks.Sum(Hours);
        var batH = bat.Sum(Hours);
        var batSessions = BatterySessions(ticks, Hours);
        var inPeriod = sessions.Count(s => s.Started < until && (s.Ended is null || s.Ended >= since));

        var unknown = ticks.Count - ac.Count - bat.Count;
        sb.AppendLine($"Guarded {Span(guardedH)} in {inPeriod} guard session{(inPeriod == 1 ? "" : "s")}; " +
                      $"on battery {Span(batH)} ({(guardedH > 0 ? 100 * batH / guardedH : 0):F0} %) in {batSessions.Count} battery session{(batSessions.Count == 1 ? "" : "s")}." +
                      (unknown > 0 ? $" {unknown} ticks (from before 0.3.0) carry no line state and count only as guarded." : ""));
        sb.AppendLine();
        sb.AppendLine("| | AC | Battery |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine($"| hours | {ac.Sum(Hours):F1} | {batH:F1} |");
        sb.AppendLine($"| package W mean / p50 / p95 | {Stats(ac, t => t.Tick.PackagePower)} | {Stats(bat, t => t.Tick.PackagePower)} |");
        sb.AppendLine($"| CPU load % mean / p95 | {MeanP95(ac, t => t.Tick.CpuLoad)} | {MeanP95(bat, t => t.Tick.CpuLoad)} |");
        sb.AppendLine($"| battery W mean / p95 | - | {MeanP95(bat, t => t.Tick.Extras?.BatW)} |");
        sb.AppendLine($"| EC CPU C mean / max | {MeanMax(ac, t => t.Tick.Extras?.EcCpuC)} | {MeanMax(bat, t => t.Tick.Extras?.EcCpuC)} |");
        sb.AppendLine($"| EC GPU C mean / max | {MeanMax(ac, t => t.Tick.Extras?.EcGpuC)} | {MeanMax(bat, t => t.Tick.Extras?.EcGpuC)} |");
        sb.AppendLine($"| fan CPU / GPU rpm mean | {Mean(ac, t => t.Tick.Extras?.FanCpu)} / {Mean(ac, t => t.Tick.Extras?.FanGpu)} | {Mean(bat, t => t.Tick.Extras?.FanCpu)} / {Mean(bat, t => t.Tick.Extras?.FanGpu)} |");
        sb.AppendLine($"| panel Hz / brightness (most of the time) | {Mode(ac, t => t.Tick.Extras?.Hz)} / {Mode(ac, t => t.Tick.Extras?.Brightness)} | {Mode(bat, t => t.Tick.Extras?.Hz)} / {Mode(bat, t => t.Tick.Extras?.Brightness)} |");
        sb.AppendLine($"| core temp max mean / max | {MeanMax(ac, t => t.Tick.Extras?.CoreTempMax)} | {MeanMax(bat, t => t.Tick.Extras?.CoreTempMax)} |");
        sb.AppendLine($"| hottest core (most of the time) | {Mode(ac, t => t.Tick.Extras?.CoreHot)} | {Mode(bat, t => t.Tick.Extras?.CoreHot)} |");
        sb.AppendLine($"| core GHz max p95 | {P95(ac, t => t.Tick.Extras?.CoreGhzMax)} | {P95(bat, t => t.Tick.Extras?.CoreGhzMax)} |");
        sb.AppendLine($"| battery W mean in use / idle | - | {Mean(bat.Where(t => InUse(t) == true).ToList(), t => t.Tick.Extras?.BatW)} / {Mean(bat.Where(t => InUse(t) == false).ToList(), t => t.Tick.Extras?.BatW)} |");
        sb.AppendLine($"| SMU read ms mean / p95 / max | {IntStats(ac, t => t.Tick.Extras?.SmuMs)} | {IntStats(bat, t => t.Tick.Extras?.SmuMs)} |");
        sb.AppendLine();

        if (batSessions.Count > 0)
        {
            sb.AppendLine("### Battery sessions");
            sb.AppendLine();
            sb.AppendLine("| Start | Hours | Wh used | Mean W | % start -> end | Mode | GPU | Hz | Brightness | In use % | dGPU h |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var s in batSessions)
                sb.AppendLine($"| {s.Start:yyyy-MM-dd HH:mm} | {s.Hours:F1} | {F1(s.WhUsed)} | {F1(s.MeanW)} | {F0(s.PctStart)} -> {F0(s.PctEnd)} | {LenovoEc.ModeName(s.PowerMode)} | {LenovoEc.IGpuModeName(s.GpuMode)} | {s.Hz?.ToString() ?? "-"} | {s.Brightness?.ToString() ?? "-"} | {F0(s.InUsePct)} | {s.DgpuHours:F1} |");
            sb.AppendLine();
        }

        Breakdown(sb, "power mode", ticks, t => t.Tick.Extras?.PowerMode is { } m ? LenovoEc.ModeName(m) : null, Hours);
        Breakdown(sb, "GPU mode", ticks, t => t.Tick.Extras?.GpuMode is { } m ? LenovoEc.IGpuModeName(m) : null, Hours);
        Breakdown(sb, "Windows overlay", ticks, t => t.Tick.Extras?.Overlay, Hours);

        var charging = ticks.Where(t => t.Tick.Extras?.ChargeW is not null).ToList();
        var modes = ticks.Where(t => t.Tick.Extras?.ChargeMode is not null).GroupBy(t => t.Tick.Extras!.ChargeMode!).OrderByDescending(g => g.Sum(Hours)).ToList();
        if (charging.Count > 0 || modes.Count > 0)
        {
            sb.Append(charging.Count > 0 ? $"Charging: {charging.Sum(Hours):F1} h at {Mean(charging, t => t.Tick.Extras?.ChargeW)} W mean" : "Charging: none");
            if (modes.Count > 0) sb.Append("; charge mode: " + string.Join(", ", modes.Select(g => $"{g.Key} {g.Sum(Hours):F1} h")));
            sb.AppendLine(".");
            sb.AppendLine();
        }

        var withDgpu = bat.Where(t => t.Tick.Extras?.Dgpu == true).ToList();
        var withoutDgpu = bat.Where(t => t.Tick.Extras?.Dgpu == false).ToList();
        if (withDgpu.Count + withoutDgpu.Count > 0)
        {
            sb.AppendLine($"dGPU on the bus {withDgpu.Sum(Hours):F1} h of the {batH:F1} h on battery; battery W mean {Mean(withDgpu, t => t.Tick.Extras?.BatW)} with it, {Mean(withoutDgpu, t => t.Tick.Extras?.BatW)} without.");
            sb.AppendLine();
        }

        var h = health.Where(x => x.Ts >= since && x.Ts < until && x.FullWh is not null).OrderBy(x => x.Ts).ToList();
        if (h.Count > 0)
        {
            var first = h[0]; var last = h[^1];
            sb.Append($"Battery health: {last.FullWh:F1} Wh full charge{(last.DesignWh is > 0 and var d ? $" ({100 * last.FullWh / d:F1} % of {d:F1} Wh design)" : "")}, {last.Cycles?.ToString() ?? "?"} cycles on {last.Ts:yyyy-MM-dd}");
            if (h.Count > 1) sb.Append($"; {last.FullWh - first.FullWh:+0.0;-0.0;0.0} Wh since {first.Ts:yyyy-MM-dd} ({h.Count} samples)");
            sb.AppendLine(".");
            sb.AppendLine();
        }

        var inWindow = events.Where(e => e.Ts >= since && e.Ts < until).ToList();
        int Count(string kind) => inWindow.Count(e => e.Kind == kind);
        sb.AppendLine($"Events: {Count("whea")} WHEA, {Count("reset")} resets, {Count("changed")} margin lost, {Count("resume")} resumes, {Count("apply-failed")} failed applies, {Count("tick-failed")} tick failures.");
        return sb.ToString();
    }

    /// <summary>Runs of consecutive ticks on battery; a hole longer than three intervals (sleep, guard down) ends one.</summary>
    public static List<BatterySession> BatterySessions(List<TickRow> ticks, Func<TickRow, double> hours)
    {
        var list = new List<BatterySession>();
        List<TickRow>? run = null;
        void Close()
        {
            if (run is null || run.Count == 0) return;
            var first = run[0].Tick; var last = run[^1].Tick;
            var whs = run.Select(t => t.Tick.Extras?.BatWh).Where(w => w is not null).Select(w => w!.Value).ToList();
            var ws = run.Select(t => t.Tick.Extras?.BatW).Where(w => w is not null).Select(w => w!.Value).ToList();
            var span = (last.Ts - first.Ts).TotalHours + hours(run[^1]);
            var known = run.Select(InUse).Where(u => u is not null).ToList();
            list.Add(new BatterySession(first.Ts, last.Ts, span, whs.Count > 1 && whs[0] >= whs[^1] ? whs[0] - whs[^1] : null,
                ws.Count > 0 ? ws.Average() : null, first.Extras?.BatPct, last.Extras?.BatPct,
                ModeOf(run, t => t.Tick.Extras?.PowerMode), ModeOf(run, t => t.Tick.Extras?.GpuMode), ModeOf(run, t => t.Tick.Extras?.Hz), ModeOf(run, t => t.Tick.Extras?.Brightness),
                known.Count > 0 ? 100.0 * known.Count(u => u == true) / known.Count : null, run.Where(t => t.Tick.Extras?.Dgpu == true).Sum(hours)));
            run = null;
        }
        foreach (var t in ticks)
        {
            if (t.Tick.Extras?.Ac != false) { Close(); continue; }
            if (run is { Count: > 0 } && (t.Tick.Ts - run[^1].Tick.Ts).TotalHours > 3 * hours(run[^1])) Close();
            (run ??= []).Add(t);
        }
        Close();
        return list;
    }

    private static void Breakdown(StringBuilder sb, string what, List<TickRow> ticks, Func<TickRow, string?> key, Func<TickRow, double> hours)
    {
        var groups = ticks.Where(t => key(t) is not null).GroupBy(t => key(t)!).OrderByDescending(g => g.Sum(hours)).ToList();
        if (groups.Count == 0) return;
        sb.AppendLine($"### By {what}");
        sb.AppendLine();
        sb.AppendLine("| Mode | Hours | On battery h | Package W mean | EC CPU C mean |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var g in groups)
            sb.AppendLine($"| {g.Key} | {g.Sum(hours):F1} | {g.Where(t => t.Tick.Extras?.Ac == false).Sum(hours):F1} | {Mean(g.ToList(), t => t.Tick.PackagePower)} | {Mean(g.ToList(), t => t.Tick.Extras?.EcCpuC)} |");
        sb.AppendLine();
    }

    private static string P95(List<TickRow> ticks, Func<TickRow, double?> f)
    {
        var xs = ticks.Select(f).Where(x => x is not null).Select(x => x!.Value).ToList();
        return xs.Count == 0 ? "-" : $"{Sampler.Percentile(xs, 0.95):F2}";
    }

    private static string IntStats(List<TickRow> ticks, Func<TickRow, int?> f)
    {
        var xs = ticks.Select(f).Where(x => x is not null).Select(x => (double)x!.Value).ToList();
        return xs.Count == 0 ? "-" : $"{xs.Average():F0} / {Sampler.Percentile(xs, 0.95):F0} / {xs.Max():F0}";
    }

    private static string MeanMax(List<TickRow> ticks, Func<TickRow, double?> f)
    {
        var xs = ticks.Select(f).Where(x => x is not null).Select(x => x!.Value).ToList();
        return xs.Count == 0 ? "-" : $"{xs.Average():F0} / {xs.Max():F0}";
    }

    private static string Stats(List<TickRow> ticks, Func<TickRow, double?> f)
    {
        var xs = ticks.Select(f).Where(x => x is not null).Select(x => x!.Value).ToList();
        return xs.Count == 0 ? "-" : $"{xs.Average():F1} / {Sampler.Percentile(xs, 0.5):F1} / {Sampler.Percentile(xs, 0.95):F1}";
    }

    private static string MeanP95(List<TickRow> ticks, Func<TickRow, double?> f)
    {
        var xs = ticks.Select(f).Where(x => x is not null).Select(x => x!.Value).ToList();
        return xs.Count == 0 ? "-" : $"{xs.Average():F1} / {Sampler.Percentile(xs, 0.95):F1}";
    }

    private static string MeanMax(List<TickRow> ticks, Func<TickRow, int?> f)
    {
        var xs = ticks.Select(f).Where(x => x is not null).Select(x => x!.Value).ToList();
        return xs.Count == 0 ? "-" : $"{xs.Average():F0} / {xs.Max()}";
    }

    private static string Mean(List<TickRow> ticks, Func<TickRow, double?> f)
    {
        var xs = ticks.Select(f).Where(x => x is not null).Select(x => x!.Value).ToList();
        return xs.Count == 0 ? "-" : $"{xs.Average():F1}";
    }

    private static string Mean(List<TickRow> ticks, Func<TickRow, int?> f)
    {
        var xs = ticks.Select(f).Where(x => x is not null).Select(x => x!.Value).ToList();
        return xs.Count == 0 ? "-" : $"{xs.Average():F0}";
    }

    private static string Mode(List<TickRow> ticks, Func<TickRow, int?> f) => ModeOf(ticks, f)?.ToString() ?? "-";

    /// <summary>The most frequent value, null when there is none.</summary>
    private static int? ModeOf(List<TickRow> ticks, Func<TickRow, int?> f)
        => ticks.Select(f).Where(x => x is not null).GroupBy(x => x).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;

    private static string Span(double hours) => hours >= 48 ? $"{hours / 24:F1} d ({hours:F0} h)" : $"{hours:F1} h";
    private static string F1(double? v) => v?.ToString("F1") ?? "-";
    private static string F0(double? v) => v?.ToString("F0") ?? "-";
}

/// <summary>`rycolab report --campaigns`: every campaign's limit per core side by side, the history of the silicon.</summary>
public static class CampaignsReport
{
    public static string Build(List<CampaignRow> campaigns, List<LimitRow> limits)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Campaigns");
        sb.AppendLine();
        if (campaigns.Count == 0)
        {
            sb.AppendLine("No campaigns in the database (`rycolab db import` brings the JSONL era in).");
            return sb.ToString();
        }
        sb.AppendLine("| Campaign | Started | Ended | Cores | Limits |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var c in campaigns)
            sb.AppendLine($"| {c.Name}{(c.Quick ? " (quick)" : "")} | {c.Started:yyyy-MM-dd HH:mm} | {(c.Ended is { } e ? e.ToString("yyyy-MM-dd HH:mm") : "unfinished")} | {c.Cores} | {limits.Count(l => l.CampaignId == c.Id)} |");
        sb.AppendLine();

        var withLimits = campaigns.Where(c => limits.Any(l => l.CampaignId == c.Id)).ToList();
        if (withLimits.Count == 0) return sb.ToString();
        var cores = limits.Select(l => l.Core).Distinct().OrderBy(c => c).ToList();
        sb.AppendLine("### Limit per core");
        sb.AppendLine();
        sb.AppendLine("| Core | " + string.Join(" | ", withLimits.Select(c => c.Name + (c.Quick ? " (quick)" : ""))) + " |");
        sb.AppendLine("|---|" + string.Concat(withLimits.Select(_ => "---|")));
        foreach (var core in cores)
            sb.AppendLine($"| {core} | " + string.Join(" | ", withLimits.Select(c =>
                limits.FirstOrDefault(l => l.CampaignId == c.Id && l.Core == core) is { } l ? (l.Margin?.ToString() ?? "none") : "-")) + " |");
        sb.AppendLine();
        return sb.ToString();
    }
}

/// <summary>`rycolab report guard`: the guard sessions and their events.</summary>
public static class GuardReport
{
    public static string Build(List<SessionRow> sessions, List<(DateTime Ts, string Kind, string Detail)> events, int lastEvents = 60)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Guard");
        sb.AppendLine();
        if (sessions.Count > 0)
        {
            sb.AppendLine("| Started | Ended | Hours | Profile | Exit |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var s in sessions.TakeLast(30))
                sb.AppendLine($"| {s.Started:yyyy-MM-dd HH:mm} | {(s.Ended is { } e ? e.ToString("yyyy-MM-dd HH:mm") : "running")} | {((s.Ended ?? DateTime.Now) - s.Started).TotalHours:F1} | {s.Profile}{(s.Adhoc ? " (ad hoc)" : "")} | {Exit(s.ExitCode)} |");
            sb.AppendLine();
        }
        var shown = events.Where(e => e.Kind != "tick").TakeLast(lastEvents).ToList();
        if (shown.Count > 0)
        {
            sb.AppendLine($"### Events (last {shown.Count})");
            sb.AppendLine();
            foreach (var (ts, kind, detail) in shown)
                sb.AppendLine($"- {ts:yyyy-MM-dd HH:mm:ss} `{kind}` {(detail.Length <= 160 ? detail : detail[..160] + "...").Replace("|", "\\|").Replace("\n", " ")}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string Exit(int? code) => code switch { null => "-", 0 => "ok", 10 => "positive", 1 => "error", var c => c.ToString()! };
}
