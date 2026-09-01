using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab plan show | init [--force] | set <key> <value>
/// config.json: baseline, engines, tests, seconds, step, start, top, safetyMargin, ycruncher.
/// </summary>
public static class PlanCommand
{
    public static int Run(Args args)
    {
        var path = args.Get("config") ?? Plan.DefaultPath;
        var sub = args.Positional.FirstOrDefault() ?? "show";

        switch (sub)
        {
            case "init":
            {
                if (File.Exists(path) && !args.Has("force")) { Console.Error.WriteLine($"{path} already exists. Use --force to overwrite it."); return 1; }
                var plan = new Plan();
                plan.Save(path);
                Console.WriteLine($"  Config written to {path}");
                return Show(plan);
            }
            case "show":
                return Show(Plan.LoadOrDefault(path));
            case "set":
            {
                if (args.Positional.Count < 3) { Console.Error.WriteLine("Usage: rycolab plan set <key> <value>   keys: base engines tests seconds step coarseStep start top safetyMargin confirmSeconds soakSeconds soakEngine ycruncher notify"); return 1; }
                var plan = Plan.LoadOrDefault(path);
                var key = args.Positional[1].ToLowerInvariant();
                var value = args.Positional[2];
                switch (key)
                {
                    case "base": plan.Base = int.Parse(value); break;
                    case "engines": plan.Engines = value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); break;
                    case "tests": plan.Tests = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); break;
                    case "seconds": plan.Seconds = int.Parse(value); break;
                    case "step": plan.Step = int.Parse(value); break;
                    case "start": plan.Start = int.Parse(value); break;
                    case "top": plan.Top = int.Parse(value); break;
                    case "safetymargin": plan.SafetyMargin = int.Parse(value); break;
                    case "coarsestep": plan.CoarseStep = int.Parse(value); break;
                    case "confirmseconds": plan.ConfirmSeconds = int.Parse(value); break;
                    case "soakseconds": plan.SoakSeconds = int.Parse(value); break;
                    case "soakengine": plan.SoakEngine = Rycolab.Core.Engines.YCruncherBinaries.Resolve(value); break;
                    case "ycruncher": plan.YCruncher = value; break;
                    case "notify": plan.Notify = bool.Parse(value); break;
                    default: Console.Error.WriteLine($"Unknown key {key}"); return 1;
                }
                plan.Save(path);
                return Show(plan);
            }
            default:
                Console.Error.WriteLine($"Unknown subcommand: {sub}");
                return 2;
        }
    }

    private static int Show(Plan plan)
    {
        Console.WriteLine();
        Console.WriteLine($"  config     {Plan.DefaultPath}{(File.Exists(Plan.DefaultPath) ? "" : "  (defaults; not written yet)")}");
        Console.WriteLine($"  baseline   {plan.Base}");
        Console.WriteLine($"  sweep      {plan.Start} -> {plan.Top} coarse {(plan.CoarseStep > 0 ? plan.CoarseStep : plan.Step)} fine {plan.Step}, {plan.Seconds} s per run, safety margin {plan.SafetyMargin}");
        Console.WriteLine($"  confirm    {plan.ConfirmSeconds} s at the limit with the sweep engines{(plan.ConfirmSeconds > 0 ? "" : "  (off)")}");
        Console.WriteLine($"  soak       {plan.SoakSeconds} s at limit + {plan.SafetyMargin} with {plan.SoakEngine}{(plan.SoakSeconds > 0 ? "" : "  (off)")}");
        Console.WriteLine($"  engines    {string.Join(" | ", plan.Engines)}");
        Console.WriteLine($"  tests      {string.Join(",", plan.Tests)}");
        Console.WriteLine($"  y-cruncher {plan.YCruncherDir}{(Installer.HasYCruncher(plan.YCruncherDir, plan.AllEngines) ? "" : "   (NOT FOUND)")}");
        Console.WriteLine($"  notify     {(plan.Notify ? "on" : "off")}  (toast + chime on WHEA / reset / margin lost / giveup)");
        Console.WriteLine();
        return 0;
    }
}
