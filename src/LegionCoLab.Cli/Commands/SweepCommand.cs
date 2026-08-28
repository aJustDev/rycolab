using LegionCoLab.Cli.Ui;
using LegionCoLab.Core;
using Spectre.Console;

namespace LegionCoLab.Cli.Commands;

/// <summary>
/// colab sweep [--plan p] [--campaign nombre] [--cores 0-15|0,3,8-11] [--start M]
/// [--top M] [--step N] [--seconds S] [--no-suspend] [--plain]
/// </summary>
public static class SweepCommand
{
    public static int Run(Args args)
    {
        var plan = Plan.Load(args.Get("plan"));
        var campaign = args.Get("campaign") ?? $"sweep-{DateTime.Now:yyyyMMdd-HHmm}";
        var dir = Path.IsPathRooted(campaign) ? campaign : Path.Combine(Plan.RepoRoot, "runs", campaign);
        var cores = ParseCores(args.Get("cores") ?? "0-15");
        if (cores is null) { Console.Error.WriteLine("--cores: usa 0-15, 0,3,8-11 ..."); return 2; }

        if (!Directory.Exists(plan.YCruncherDir))
        {
            Console.Error.WriteLine($"No existe {plan.YCruncherDir}. Copia los binarios de y-cruncher (README).");
            return 1;
        }

        var options = new SweepOptions
        {
            Cores = cores,
            Start = args.GetInt("start"),
            Top = args.GetInt("top"),
            Step = args.GetInt("step"),
            Seconds = args.GetInt("seconds"),
            Suspend = !args.Has("no-suspend"),
            CampaignDir = dir,
        };

        using var co = new CoController();
        if (!co.IsPsmSupported) { Console.Error.WriteLine("Este SMU no soporta SetDldoPsmMargin."); return 1; }
        var before = co.ReadAll();
        var offBase = before.Where(r => r.Margin != plan.Base).Select(r => r.Index).ToList();
        if (offBase.Count > 0)
        {
            Console.Error.WriteLine($"El hardware no esta en la base {plan.Base} (nucleos {string.Join(",", offBase)}). Ejecuta 'colab reset --to {plan.Base}' o cierra guard antes de barrer.");
            return 2;
        }

        using var telemetry = new Telemetry();
        var pm = new PmTable(co.Cpu);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        Console.WriteLine($"  campana {dir}");

        if (args.Has("plain"))
        {
            var sweep = new Sweep(co, plan, options, telemetry, pm, new PlainSweepSink());
            return sweep.Run(cts.Token);
        }

        var known = Journal.ReadJsonFile<Dictionary<string, int?>>(Path.Combine(dir, "limits.json"))?
                        .ToDictionary(k => int.Parse(k.Key), k => k.Value) ?? [];
        var view = new SweepView(plan, cores, options.Seconds ?? plan.Seconds, known);
        var code = 0;
        AnsiConsole.Live(view.Render()).AutoClear(false).Start(ctx =>
        {
            view.Changed = () => { ctx.UpdateTarget(view.Render()); ctx.Refresh(); };
            var sweep = new Sweep(co, plan, options, telemetry, pm, view);
            code = sweep.Run(cts.Token);
        });
        return code;
    }

    /// <summary>"0-15", "0,3,8-11", "11".</summary>
    public static int[]? ParseCores(string spec)
    {
        var set = new SortedSet<int>();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-', 2);
            if (range.Length == 2 && int.TryParse(range[0], out var a) && int.TryParse(range[1], out var b) && a <= b)
                for (var i = a; i <= b; i++) set.Add(i);
            else if (int.TryParse(part, out var n)) set.Add(n);
            else return null;
        }
        if (set.Count == 0 || set.Min < 0 || set.Max >= Topology.MaxCores) return null;
        return [.. set];
    }
}
