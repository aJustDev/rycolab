using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab profile show | from-sweep <campaign> [--margin N] | import --cores a,...,p
/// --campaign <name> [--limits a,...,p] [--note text] | export <path>
/// </summary>
public static class ProfileCommand
{
    public static int Run(Args args)
    {
        var sub = args.Positional.FirstOrDefault() ?? "show";
        switch (sub)
        {
            case "show":
            {
                if (!Profile.Exists()) { Console.WriteLine("  No profile."); return 0; }
                return Show(Profile.Load());
            }
            case "from-sweep":
            {
                if (args.Positional.Count < 2) { Console.Error.WriteLine("Usage: rycolab profile from-sweep <campaign> [--margin 5]"); return 1; }
                var dir = AppPaths.Campaign(args.Positional[1]);
                var limits = Journal.ReadJsonFile<Dictionary<string, int?>>(Path.Combine(dir, "limits.json"));
                if (limits is null) { Console.Error.WriteLine($"No limits.json in {dir}"); return 1; }
                var config = Plan.LoadOrDefault();
                using var co = new CoController();
                var profile = Profile.FromLimits(limits.ToDictionary(k => int.Parse(k.Key), k => k.Value), config,
                    Path.GetFileName(dir.TrimEnd('\\', '/')), CpuFingerprint.Of(co), args.GetInt("margin"));
                var missing = Enumerable.Range(0, co.CoreCount).Where(c => profile.Source!.Limits[c] is null).ToList();
                profile.Save();
                Console.WriteLine($"  Profile = limit + {profile.Source!.SafetyMargin} from {dir}, saved to {AppPaths.Profile}");
                if (missing.Count > 0) Console.WriteLine($"  Cores without a limit stay at the baseline {config.Base}: {string.Join(",", missing)}");
                Show(profile);
                Console.WriteLine("  Apply it with `rycolab on`.");
                return 0;
            }
            case "import":
            {
                var coresSpec = args.Get("cores");
                var campaign = args.Get("campaign");
                if (coresSpec is null || campaign is null) { Console.Error.WriteLine("Usage: rycolab profile import --cores a,...,p --campaign <name> [--limits a,...,p] [--note text]"); return 1; }
                var config = Plan.LoadOrDefault();
                using var co = new CoController();
                // Fewer than 16 values (an 8-core CPU): the rest stay at the baseline / unknown.
                var given = coresSpec.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                var cores = Enumerable.Range(0, Topology.MaxCores).Select(c => c < given.Length ? given[c] : config.Base).ToArray();
                var givenLimits = args.Get("limits")?.Split(',').Select(s => (int?)int.Parse(s.Trim())).ToArray();
                var limits = givenLimits is null ? null : Enumerable.Range(0, Topology.MaxCores).Select(c => c < givenLimits.Length ? givenLimits[c] : null).ToArray();
                var profile = new Profile
                {
                    Cores = cores, Base = config.Base, Fingerprint = CpuFingerprint.Of(co),
                    Source = new ProfileSource
                    {
                        Campaign = campaign, Date = DateTime.Now, SafetyMargin = config.SafetyMargin,
                        Engines = config.Engines, Tests = config.Tests, Seconds = config.Seconds,
                        ConfirmSeconds = config.ConfirmSeconds, SoakSeconds = config.SoakSeconds, SoakEngine = config.SoakSeconds > 0 ? config.SoakEngine : null,
                        Limits = limits ?? new int?[Topology.MaxCores], Note = args.Get("note"),
                    },
                };
                profile.Save();
                Console.WriteLine($"  Profile imported to {AppPaths.Profile}");
                return Show(profile);
            }
            case "export":
            {
                if (args.Positional.Count < 2) { Console.Error.WriteLine("Usage: rycolab profile export <path>"); return 1; }
                var p = Profile.Load();
                p.Save(Path.GetFullPath(args.Positional[1]));
                Console.WriteLine($"  Written {Path.GetFullPath(args.Positional[1])}");
                return 0;
            }
            default:
                Console.Error.WriteLine($"Unknown subcommand: {sub}");
                return 2;
        }
    }

    private static int Show(Profile p)
    {
        Console.WriteLine();
        var count = p.Fingerprint?.Cores is > 0 and var n ? n : Topology.MaxCores;
        foreach (var line in Ui.CoreRows.Lines(count, c => $"{c}:{p.Cores[c]}")) Console.WriteLine($"  {line}");
        Console.WriteLine($"  baseline {p.Base}   CPU {p.Fingerprint?.CpuName ?? "?"} ({p.Fingerprint?.Cores.ToString() ?? "?"} cores)");
        if (p.Source is { } s)
        {
            Console.WriteLine($"  source   {s.Campaign}, {s.Date:yyyy-MM-dd HH:mm}, limit + {s.SafetyMargin}, engines {string.Join(" | ", s.Engines)}, tests {string.Join(",", s.Tests)}, {s.Seconds} s" +
                              (s.ConfirmSeconds > 0 ? $", confirmed {s.ConfirmSeconds} s" : "") + (s.SoakSeconds > 0 ? $", soaked {s.SoakSeconds} s with {s.SoakEngine}" : ""));
            Console.WriteLine($"  limits   {string.Join("   ", Ui.CoreRows.Lines(count, c => c < s.Limits.Length ? s.Limits[c]?.ToString() ?? "-" : "-", " "))}");
            if (s.Note is not null) Console.WriteLine($"  note     {s.Note}");
        }
        else Console.WriteLine("  source   NONE (`rycolab on` will refuse it)");
        Console.WriteLine();
        return 0;
    }
}
