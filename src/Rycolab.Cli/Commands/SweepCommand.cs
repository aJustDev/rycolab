using Rycolab.Cli.Ui;
using Rycolab.Core;
using Profile = Rycolab.Core.Profile;
using Spectre.Console;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab sweep [--campaign name] [--cores 0-15|0,3,8-11] [--engines zn5,p4p]
/// [--start M] [--top M] [--step N] [--seconds S] [--no-suspend] [--plain]
/// </summary>
public static class SweepCommand
{
    public static int Run(Args args)
    {
        var config = Plan.LoadOrDefault();
        var campaign = args.Get("campaign") ?? $"sweep-{DateTime.Now:yyyyMMdd-HHmm}";
        var dir = AppPaths.Campaign(campaign);
        var cores = args.Get("cores") is { } spec ? ParseCores(spec) : null;
        if (args.Get("cores") is not null && cores is null) { Console.Error.WriteLine("--cores: use 0-15, 0,3,8-11 ..."); return 2; }
        if (FindCommand.ParseEngines(args, config) is { } engineError) { Console.Error.WriteLine(engineError); return 2; }

        if (!Installer.HasYCruncher(config.YCruncherDir, config.Engines))
        {
            Console.Error.WriteLine($"y-cruncher binaries not found in {config.YCruncherDir}. Run `rycolab install`.");
            return 1;
        }
        if (Service.GuardProcess() is not null)
        {
            Console.Error.WriteLine("A guard is running. Run `rycolab off` first: the sweep needs the baseline.");
            return 2;
        }

        using var co = new CoController();
        if (!co.IsPsmSupported) { Console.Error.WriteLine("This SMU does not support SetDldoPsmMargin."); return 1; }
        cores ??= Enumerable.Range(0, co.CoreCount).ToArray();
        if (cores.Max() >= co.CoreCount) { Console.Error.WriteLine($"--cores: this CPU has {co.CoreCount} cores (0-{co.CoreCount - 1})."); return 2; }

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
        var before = co.ReadAll();
        var offBase = before.Where(r => r.Margin != config.Base).Select(r => r.Index).ToList();
        if (offBase.Count > 0)
        {
            Console.Error.WriteLine($"The hardware is not at the baseline {config.Base} (cores {string.Join(",", offBase)}). Run `rycolab off` or `rycolab reset --to {config.Base}` first.");
            return 2;
        }
        if (!Safety.IsOnAcPower()) { Console.Error.WriteLine("Not on AC power."); return 2; }

        AppPaths.EnsureData();
        File.WriteAllText(AppPaths.CurrentCampaign, dir);

        using var telemetry = new Telemetry();
        var pm = new PmTable(co.Cpu);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        Console.WriteLine($"  campaign {dir}");

        if (args.Has("plain"))
        {
            var sweep = new Sweep(co, config, options, telemetry, pm, new PlainSweepSink());
            return sweep.Run(cts.Token);
        }

        var known = Journal.ReadJsonFile<Dictionary<string, int?>>(Path.Combine(dir, "limits.json"))?
                        .ToDictionary(k => int.Parse(k.Key), k => k.Value) ?? [];
        var view = new SweepView(cores, options.Seconds ?? config.Seconds, known);
        var code = 0;
        AnsiConsole.Live(view.Render()).AutoClear(false).Start(ctx =>
        {
            view.Changed = () => { ctx.UpdateTarget(view.Render()); ctx.Refresh(); };
            var sweep = new Sweep(co, config, options, telemetry, pm, view);
            code = sweep.Run(cts.Token);
        });
        if (code == 0) Console.WriteLine($"  Done. Next: rycolab profile from-sweep {Path.GetFileName(dir.TrimEnd('\\', '/'))}");
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
