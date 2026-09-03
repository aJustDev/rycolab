using Rycolab.Core.Legion;
using System.Text;
using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab report [<campaign>|guard] [--md[=path]]
/// rycolab report --power [--since 30d | --month 2026-08] | --campaigns | --health [--md[=path]]
/// Per-core limits with V/GHz/W at the limit, positives with time to error,
/// WHEA and events; `guard` the sessions and events; --power the ticks over
/// a period; --campaigns every campaign's limits; --health the daily battery
/// capacity. Everything from the database. Default: the current campaign.
/// No elevation.
/// </summary>
public static class ReportCommand
{
    public static int Run(Args args)
    {
        if (args.Get("bench") is { } bench) return Bench(bench, args.Get("vs"), args.GetInt("min-power") ?? 100, args.Has("battery"), args.Get("md"), args.Has("md"));
        if (args.Has("health")) return Health(args);
        if (args.Has("power")) return Power(args);
        if (args.Has("campaigns")) return Campaigns(args);

        var name = args.Positional.FirstOrDefault() ?? args.Get("campaign");
        if (name is null)
        {
            if (!File.Exists(AppPaths.CurrentCampaign)) { Console.Error.WriteLine("No campaign yet. Usage: rycolab report <campaign>|guard"); return 2; }
            name = File.ReadAllText(AppPaths.CurrentCampaign).Trim();
        }
        name = Path.GetFileName(name.TrimEnd('\\', '/'));

        using var store = Store.Open();
        string md;
        if (name == "guard")
            md = GuardReport.Build(store.Sessions(), store.Events("guard"));
        else
        {
            if (store.CampaignId(name) is not { } id)
            {
                Console.Error.WriteLine($"No campaign named {name} in the database (`rycolab report --campaigns` lists them; `rycolab db import` brings the JSONL era in).");
                return 1;
            }
            md = Build(name, store.Runs(id), store.Limits(id).ToDictionary(k => k.Key.ToString(), k => k.Value), store.Events("sweep", id));
        }

        if (args.Has("md"))
        {
            var target = args.Get("md") ?? Path.Combine(name == "guard" ? AppPaths.Guard : AppPaths.Campaign(name), "report.md");
            File.WriteAllText(target, md, new UTF8Encoding(false));
            Console.WriteLine($"  Written {target}");
        }
        else Console.WriteLine(md);
        return 0;
    }

    public static string Build(string name, List<RunResult> runs, Dictionary<string, int?> limits, List<(DateTime Ts, string Kind, string Detail)> events)
    {
        var sb = new StringBuilder();
        var cores = runs.Select(r => r.Core).Concat(limits.Keys.Select(int.Parse)).Distinct().OrderBy(c => c).ToList();
        var engines = runs.Select(r => r.Engine).Distinct().ToList();
        var first = runs.Count > 0 ? runs.Min(r => r.Started) : (DateTime?)null;
        var last = runs.Count > 0 ? runs.Max(r => r.Ended) : (DateTime?)null;

        sb.AppendLine($"## Campaign {name}");
        sb.AppendLine();
        if (first is not null) sb.AppendLine($"{first:yyyy-MM-dd HH:mm} - {last:HH:mm}, {runs.Count} runs, engines {string.Join(" | ", engines)}.");
        sb.AppendLine();

        if (cores.Count > 0)
        {
            sb.AppendLine("### Limits");
            sb.AppendLine();
            sb.AppendLine("| Core | CCD | Limit | Margins tried (engine: verdict, s) | V / GHz / W at the limit |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var c in cores)
            {
                var lim = limits.TryGetValue(c.ToString(), out var l) ? l : null;
                var hist = string.Join("; ", runs.Where(r => r.Core == c).GroupBy(r => r.Margin).OrderBy(g => g.Key)
                    .Select(g => $"{g.Key}: " + string.Join(", ", g.Select(r => $"{Short(r.Engine)}{(r.Stage == "sweep" ? "" : " " + r.Stage)} {Mark(r.Verdict)} {r.Seconds}s"))));
                var at = runs.FirstOrDefault(r => r.Core == c && r.Margin == lim && r.Verdict == "clean" && r.Telemetry?.VoltMedian is not null && r.Engine.StartsWith("04"))
                         ?? runs.FirstOrDefault(r => r.Core == c && r.Margin == lim && r.Verdict == "clean" && r.Telemetry?.VoltMedian is not null);
                var tele = at?.Telemetry is { } t ? $"{t.VoltMedian:F3} / {t.FreqMedian:F3} / {t.PowerMedian:F1}" : "-";
                sb.AppendLine($"| {c} | {Topology.CcdName(c)} | {(lim?.ToString() ?? (limits.ContainsKey(c.ToString()) ? "none" : "pending"))} | {hist} | {tele} |");
            }
            sb.AppendLine();
            sb.AppendLine("```");
            foreach (var line in Ui.CoreRows.Lines(Ui.CoreRows.CountFor(cores), c => $"{c}:{Lim(limits, c)}")) sb.AppendLine(line);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        var positives = runs.Where(r => r.Verdict is not ("clean" or "aborted")).ToList();
        sb.AppendLine($"### Positives ({positives.Count})");
        sb.AppendLine();
        if (positives.Count > 0)
        {
            sb.AppendLine("| Time | Core | Margin | Engine | Stage | Signal | After | Detail |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var p in positives)
                sb.AppendLine($"| {p.Started:HH:mm:ss} | {p.Core} | {p.Margin} | {p.Engine} | {p.Stage} | {p.Verdict}{(p.ExitCode is { } x ? $" (exit {x})" : "")} | {p.Seconds} s | {Escape(Trunc(p.Error ?? "", 120))} |");
            sb.AppendLine();
        }

        var whea = runs.Sum(r => r.Whea);
        sb.AppendLine($"WHEA during the runs: {whea}.");
        sb.AppendLine();

        if (events.Count > 0)
        {
            sb.AppendLine("### Events");
            sb.AppendLine();
            foreach (var (ts, kind, detail) in events.Where(e => e.Kind != "tick"))
                sb.AppendLine($"- {ts:yyyy-MM-dd HH:mm:ss} `{kind}` {Escape(Trunc(detail, 160))}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>rycolab report --power [--since 30d|7d|24h | --month 2026-08] [--md[=path]]: the guard's ticks over a period.</summary>
    private static int Power(Args args)
    {
        if (PowerReport.Period(args.Get("since"), args.Get("month"), DateTime.Now) is not { } p)
        {
            Console.Error.WriteLine("  --since takes 30d, 7d, 2w or 24h; --month takes 2026-08.");
            return 2;
        }
        using var store = Store.Open();
        var text = PowerReport.Build(p.Label, p.Since, p.Until, store.Ticks(p.Since, p.Until), store.Sessions(p.Since),
            store.Events("guard", null, p.Since), store.Health());
        return Emit(text, args, Path.Combine(AppPaths.Guard, "power.md"));
    }

    /// <summary>rycolab report --campaigns [--md[=path]]: every campaign and its limits per core.</summary>
    private static int Campaigns(Args args)
    {
        using var store = Store.Open();
        return Emit(CampaignsReport.Build(store.Campaigns(), store.AllLimits()), args, Path.Combine(AppPaths.Campaigns, "campaigns.md"));
    }

    private static int Emit(string text, Args args, string defaultTarget)
    {
        if (args.Has("md"))
        {
            var target = args.Get("md") ?? defaultTarget;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text, new UTF8Encoding(false));
            Console.WriteLine($"  Written {target}");
        }
        else Console.WriteLine(text);
        return 0;
    }

    /// <summary>rycolab report --health [--md [path]]: the guard's daily battery capacity samples.</summary>
    private static int Health(Args args)
    {
        var dir = AppPaths.Guard;
        using var store = Store.Open();
        var samples = store.Health();
        if (samples.Count == 0) { Console.Error.WriteLine("  No health samples yet: the guard takes one per day while it runs."); return 1; }

        var sb = new StringBuilder();
        var now = samples[^1];
        var start = samples[0];
        sb.AppendLine("## Battery health");
        sb.AppendLine();
        sb.AppendLine($"Now: {Wh(now.FullWh)} Wh full charge{(now.DesignWh is { } d ? $" of {d:F1} Wh design ({Pct(now.FullWh, now.DesignWh)})" : "")}, {now.Cycles?.ToString() ?? "?"} cycles.");
        if (samples.Count > 1 && now.FullWh is { } nf && start.FullWh is { } sf)
            sb.AppendLine($"Since {start.Ts:yyyy-MM-dd}: {nf - sf:+0.0;-0.0;0.0} Wh ({(nf - sf) / sf * 100:+0.0;-0.0;0.0} %) over {(now.Ts - start.Ts).TotalDays:F0} days, {samples.Count} samples.");
        sb.AppendLine();
        sb.AppendLine("| Date | Full Wh | % of design | Cycles |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var s in samples)
            sb.AppendLine($"| {s.Ts:yyyy-MM-dd} | {Wh(s.FullWh)} | {Pct(s.FullWh, s.DesignWh)} | {s.Cycles?.ToString() ?? "-"} |");
        var text = sb.ToString();

        if (args.Has("md"))
        {
            var target = args.Get("md") ?? Path.Combine(dir, "health.md");
            File.WriteAllText(target, text, new UTF8Encoding(false));
            Console.WriteLine($"  Written {target}");
        }
        else Console.WriteLine(text);
        return 0;

        static string Wh(double? v) => v?.ToString("F1") ?? "-";
        static string Pct(double? full, double? design) => full is { } f && design is > 0 and var g ? $"{100.0 * f / g:F1} %" : "-";
    }

    /// <summary>rycolab report --bench log.csv [--vs base.csv] [--min-power 100 | --battery] [--md [path]]: aggregates of a `dev log` CSV over the loaded (or on-battery) samples.</summary>
    private static int Bench(string path, string? vs, int minPower, bool battery, string? mdPath, bool md)
    {
        if (!File.Exists(path)) { Console.Error.WriteLine($"Not found: {path}"); return 1; }
        if (vs is not null && !File.Exists(vs)) { Console.Error.WriteLine($"Not found: {vs}"); return 1; }
        bool Loaded(Dictionary<string, double> row) => row.TryGetValue(BenchLog.PackagePower, out var p) && p > minPower;
        bool OnBattery(Dictionary<string, double> row) => row.TryGetValue(BenchLog.Ac, out var ac) && ac == 0 && row.TryGetValue(BenchLog.BatteryW, out var w) && w > 0;
        Func<Dictionary<string, double>, bool> filter = battery ? OnBattery : Loaded;
        var filterName = battery ? "the machine on battery" : $"{BenchLog.PackagePower} > {minPower}";
        var d = BenchLog.Read(path, filter, out var rows, out var kept);
        Dictionary<string, List<double>>? b = null;
        if (vs is not null) b = BenchLog.Read(vs, filter, out _, out _);
        var name = Path.GetFileNameWithoutExtension(path);
        var text = $"## Bench {name}{(vs is null ? "" : $" vs {Path.GetFileNameWithoutExtension(vs)}")}\n\n"
                   + BenchLog.Summary(name, d, rows, kept, filterName, vs is null ? null : Path.GetFileNameWithoutExtension(vs), b);
        if (md)
        {
            var target = mdPath ?? Path.ChangeExtension(path, ".md");
            File.WriteAllText(target, text, new UTF8Encoding(false));
            Console.WriteLine($"  Written {target}");
        }
        else Console.WriteLine(text);
        return 0;
    }

    private static string Lim(Dictionary<string, int?> limits, int c)
        => limits.TryGetValue(c.ToString(), out var l) ? (l?.ToString() ?? "x") : "?";
    private static string Short(string engine) => engine.Split(' ')[0];
    private static string Mark(string v) => v switch { "clean" => "ok", "error" => "ERR", "crashed" => "CRASH", "whea" => "WHEA", "hang" => "HANG", _ => v };
    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "...";
    private static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ");
}
