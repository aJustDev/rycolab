using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab plan init [--from-hardware] | show | set-core N M | set-profile a,b,...,p
/// | from-sweep <campaign> [--margin N]   [--plan path]
/// </summary>
public static class PlanCommand
{
    public static int Run(Args args)
    {
        var path = args.Get("plan") ?? Plan.DefaultPath;
        var sub = args.Positional.FirstOrDefault() ?? "show";

        switch (sub)
        {
            case "init":
            {
                if (File.Exists(path) && !args.Has("force"))
                {
                    Console.Error.WriteLine($"{path} already exists. Use --force to overwrite it.");
                    return 1;
                }
                var plan = new Plan();
                if (args.Has("from-hardware"))
                {
                    using var co = new CoController();
                    plan.Profile = co.ReadAll().Select(r => r.Margin ?? plan.Base).ToArray();
                }
                plan.Save(path);
                Console.WriteLine($"  Plan written to {path}");
                return Show(plan);
            }
            case "show":
                return Show(Plan.Load(path));
            case "set-core":
            {
                if (args.Positional.Count < 3 || !int.TryParse(args.Positional[1], out var core) || !int.TryParse(args.Positional[2], out var margin))
                {
                    Console.Error.WriteLine("Usage: rycolab plan set-core <core> <margin>");
                    return 1;
                }
                var plan = Plan.Load(path);
                if (core < 0 || core >= plan.Profile.Length) { Console.Error.WriteLine($"core {core} out of range"); return 1; }
                plan.Profile[core] = margin;
                plan.Save(path);
                return Show(plan);
            }
            case "set-profile":
            {
                if (args.Positional.Count < 2)
                {
                    Console.Error.WriteLine("Usage: rycolab plan set-profile -35,-35,...   (16 values)");
                    return 1;
                }
                var plan = Plan.Load(path);
                plan.Profile = args.Positional[1].Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                plan.Save(path);
                return Show(plan);
            }
            case "from-sweep":
            {
                if (args.Positional.Count < 2)
                {
                    Console.Error.WriteLine("Usage: rycolab plan from-sweep <campaign> [--margin 5]");
                    return 1;
                }
                var campaign = args.Positional[1];
                var dir = Path.IsPathRooted(campaign) ? campaign : Path.Combine(Plan.RepoRoot, "runs", campaign);
                var limits = Journal.ReadJsonFile<Dictionary<string, int?>>(Path.Combine(dir, "limits.json"));
                if (limits is null) { Console.Error.WriteLine($"No limits.json in {dir}"); return 1; }

                var plan = File.Exists(path) ? Plan.Load(path) : new Plan();
                var margin = args.GetInt("margin") ?? plan.SafetyMargin;
                var missing = new List<int>();
                for (var c = 0; c < plan.Profile.Length; c++)
                {
                    if (limits.TryGetValue(c.ToString(), out var lim) && lim is { } l)
                        plan.Profile[c] = Math.Min(plan.Top, l + margin);
                    else { plan.Profile[c] = plan.Base; missing.Add(c); }
                }
                plan.Save(path);
                Console.WriteLine($"  Profile = limit + {margin} from {dir}");
                if (missing.Count > 0) Console.WriteLine($"  No limit (left at the baseline {plan.Base}): {string.Join(",", missing)}");
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
        Console.WriteLine($"  CCD0  {string.Join("  ", plan.Profile.Take(8).Select((m, i) => $"{i}:{m}"))}");
        Console.WriteLine($"  CCD1  {string.Join("  ", plan.Profile.Skip(8).Select((m, i) => $"{i + 8}:{m}"))}");
        Console.WriteLine();
        Console.WriteLine($"  baseline {plan.Base}   sweep {plan.Start} -> {plan.Top} step {plan.Step}, {plan.Seconds} s   safety margin {plan.SafetyMargin}");
        Console.WriteLine($"  engines {string.Join(" | ", plan.Engines)}   tests {string.Join(",", plan.Tests)}");
        Console.WriteLine($"  y-cruncher {plan.YCruncherDir}{(Directory.Exists(plan.YCruncherDir) ? "" : "   (NOT FOUND)")}");
        Console.WriteLine();
        return 0;
    }
}
