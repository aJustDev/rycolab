using System.Text;
using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab report [<campaign>|guard] [--md [path]] [--rebuild]
/// Per-core limits with V/GHz/W at the limit, positives with time to error,
/// WHEA and events. Default: the current campaign. No elevation.
/// </summary>
public static class ReportCommand
{
    public static int Run(Args args)
    {
        var name = args.Positional.FirstOrDefault() ?? args.Get("campaign");
        string dir;
        if (name is null)
        {
            if (!File.Exists(AppPaths.CurrentCampaign)) { Console.Error.WriteLine("No campaign yet. Usage: rycolab report <campaign>|guard"); return 2; }
            dir = File.ReadAllText(AppPaths.CurrentCampaign).Trim();
        }
        else dir = name == "guard" ? AppPaths.Guard : AppPaths.Campaign(name);
        if (!Directory.Exists(dir)) { Console.Error.WriteLine($"Not found: {dir}"); return 1; }

        var dbPath = Path.Combine(dir, "rycolab.db");
        var rebuild = args.Has("rebuild") || !File.Exists(dbPath);
        // A running guard holds its database; read from a copy of the journal instead.
        if (dir == AppPaths.Guard && Service.GuardProcess() is not null) rebuild = true;
        var work = dbPath;
        if (rebuild && dir == AppPaths.Guard && Service.GuardProcess() is not null)
            work = Path.Combine(Path.GetTempPath(), "rycolab-report.db");
        using var store = new Store(work);
        if (rebuild) store.Rebuild(dir);

        var runs = store.Runs();
        var limits = Journal.ReadJsonFile<Dictionary<string, int?>>(Path.Combine(dir, "limits.json")) ?? [];
        var md = Build(Path.GetFileName(dir.TrimEnd('\\', '/')), runs, limits, store.Events());

        if (args.Has("md"))
        {
            var target = args.Get("md") ?? Path.Combine(dir, "report.md");
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
                    .Select(g => $"{g.Key}: " + string.Join(", ", g.Select(r => $"{Short(r.Engine)} {Mark(r.Verdict)} {r.Seconds}s"))));
                var at = runs.FirstOrDefault(r => r.Core == c && r.Margin == lim && r.Verdict == "clean" && r.Telemetry?.VoltMedian is not null && r.Engine.StartsWith("04"))
                         ?? runs.FirstOrDefault(r => r.Core == c && r.Margin == lim && r.Verdict == "clean" && r.Telemetry?.VoltMedian is not null);
                var tele = at?.Telemetry is { } t ? $"{t.VoltMedian:F3} / {t.FreqMedian:F3} / {t.PowerMedian:F1}" : "-";
                sb.AppendLine($"| {c} | {Topology.CcdName(c)} | {(lim?.ToString() ?? (limits.ContainsKey(c.ToString()) ? "none" : "pending"))} | {hist} | {tele} |");
            }
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine("CCD0   " + string.Join("  ", Enumerable.Range(0, 8).Select(c => $"{c}:{Lim(limits, c)}")));
            sb.AppendLine("CCD1   " + string.Join("  ", Enumerable.Range(8, 8).Select(c => $"{c}:{Lim(limits, c)}")));
            sb.AppendLine("```");
            sb.AppendLine();
        }

        var positives = runs.Where(r => r.Verdict is not ("clean" or "aborted")).ToList();
        sb.AppendLine($"### Positives ({positives.Count})");
        sb.AppendLine();
        if (positives.Count > 0)
        {
            sb.AppendLine("| Time | Core | Margin | Engine | Signal | After | Detail |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var p in positives)
                sb.AppendLine($"| {p.Started:HH:mm:ss} | {p.Core} | {p.Margin} | {p.Engine} | {p.Verdict}{(p.ExitCode is { } x ? $" (exit {x})" : "")} | {p.Seconds} s | {Escape(Trunc(p.Error ?? "", 120))} |");
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

    private static string Lim(Dictionary<string, int?> limits, int c)
        => limits.TryGetValue(c.ToString(), out var l) ? (l?.ToString() ?? "x") : "?";
    private static string Short(string engine) => engine.Split(' ')[0];
    private static string Mark(string v) => v switch { "clean" => "ok", "error" => "ERR", "crashed" => "CRASH", "whea" => "WHEA", "hang" => "HANG", _ => v };
    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "...";
    private static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ");
}
