using Rycolab.Cli.Ui;
using Rycolab.Core;
using Profile = Rycolab.Core.Profile;
using Spectre.Console;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab find [--quick] [--cores 0-15] [--engines zn5,p4p] [--resume] [--yes] [--accept] [--plain]
/// The sweep with a wizard around it: checks, time estimate, confirmation,
/// campaign bookkeeping, and the proposed profile at the end.
/// </summary>
public static class FindCommand
{
    private static readonly string[] QuickTests = ["SFTv4", "FFTv4", "N63"];
    private const int QuickSeconds = 180, QuickConfirmSeconds = 300, QuickSoakSeconds = 120;

    public static int Run(Args args)
    {
        Console.WriteLine();
        var config = Plan.LoadOrDefault();
        var quick = args.Has("quick");
        if (quick)
        {
            config.Tests = QuickTests; config.Seconds = QuickSeconds;
            config.ConfirmSeconds = Math.Min(config.ConfirmSeconds, QuickConfirmSeconds);
            config.SoakSeconds = Math.Min(config.SoakSeconds, QuickSoakSeconds);
        }
        if (ParseEngines(args, config) is { } engineError) { Console.Error.WriteLine(engineError); return 2; }

        // The controller only reads here; the core universe is what this CPU has.
        using var co = new CoController();
        var coreCount = co.CoreCount;
        var cores = SweepCommand.ParseCores(args.Get("cores") ?? $"0-{coreCount - 1}");
        if (cores is null) { Console.Error.WriteLine($"  --cores: use 0-{coreCount - 1}, 0,3,5-7 ..."); return 2; }
        if (cores.Max() >= coreCount) { Console.Error.WriteLine($"  --cores: this CPU has {coreCount} cores (0-{coreCount - 1})."); return 2; }

        // ---- checks ----
        var problems = new List<string>();
        if (!co.IsPsmSupported) problems.Add($"this CPU's SMU ({co.SmuType}) does not expose per-core Curve Optimizer.");
        if (co.TopologyWarning is { } tw) problems.Add($"{tw} (`rycolab dev probe` shows the details).");
        if (!Installer.HasYCruncher(config.YCruncherDir, config.AllEngines)) problems.Add($"y-cruncher binaries missing in {config.YCruncherDir} ({string.Join(", ", config.Engines)}): run `rycolab install`.");
        if (!Safety.IsOnAcPower()) problems.Add("not on AC power: plug the charger in.");
        if (problems.Count > 0)
        {
            foreach (var p in problems) Console.Error.WriteLine($"  {p}");
            return 2;
        }

        if (Service.GuardProcess() is not null)
        {
            Console.WriteLine("  A guard is running; stopping it (the sweep needs the baseline)...");
            if (!Service.Stop()) { Console.Error.WriteLine("  the guard did not stop."); return 1; }
            Service.Disable();
        }

        // ---- campaign: new or resume ----
        string dir;
        var resume = args.Has("resume");
        using var store = Store.Open();
        var current = File.Exists(AppPaths.CurrentCampaign) ? File.ReadAllText(AppPaths.CurrentCampaign).Trim() : null;
        var currentId = current is null ? null : store.CampaignId(Path.GetFileName(current.TrimEnd('\\', '/')));
        var currentUnfinished = currentId is { } cid && !cores.All(store.Limits(cid).ContainsKey);
        if (resume)
        {
            if (!currentUnfinished)
            {
                // The auto-resume task lands here after the campaign is done; leave quietly and clean up.
                Console.Error.WriteLine("  Nothing to resume.");
                Service.RemoveFindResume();
                return 0;
            }
            dir = current!;
        }
        else if (currentUnfinished && !args.Has("new"))
        {
            Console.WriteLine($"  There is an unfinished campaign: {current}");
            if (store.RunningRun(currentId!.Value) is not null)
                Console.WriteLine("  It has a run in progress: the machine hung or rebooted during it (that counts as a positive).");
            if (!Ask("  Resume it? [Y/n] ", args, defaultYes: true)) dir = AppPaths.Campaign($"find-{DateTime.Now:yyyyMMdd-HHmm}");
            else dir = current!;
        }
        else dir = AppPaths.Campaign($"find-{DateTime.Now:yyyyMMdd-HHmm}");

        var known = store.CampaignId(Path.GetFileName(dir.TrimEnd('\\', '/'))) is { } knownId ? store.Limits(knownId) : [];
        var pending = cores.Where(c => !known.ContainsKey(c)).ToArray();

        // ---- estimate ----
        var coarse = config.CoarseStep > 0 ? Math.Max(config.Step, config.CoarseStep) : config.Step;
        var coarseMargins = (config.Top - config.Start) / coarse + 1;
        var fineRuns = coarse / config.Step - 1;
        var perRun = (config.Seconds + 20) * config.Engines.Length;
        var extras = config.ConfirmSeconds * config.Engines.Length + config.SoakSeconds + 40;   // one confirmation, one soak
        var typical = TimeSpan.FromSeconds((double)pending.Length * ((2 + fineRuns) * perRun + extras));
        var worst = TimeSpan.FromSeconds((double)pending.Length * ((coarseMargins + fineRuns) * perRun + 2 * extras));
        Console.WriteLine($"  Campaign   {dir}");
        Console.WriteLine($"  Cores      {pending.Length} pending of {cores.Length} ({string.Join(",", pending)})");
        Console.WriteLine($"  Per run    {config.Seconds} s, engines {string.Join(" | ", config.Engines)}, tests {string.Join(",", config.Tests)}{(quick ? "  (quick)" : "")}");
        Console.WriteLine($"  Margins    {config.Start} -> {config.Top} coarse {coarse} fine {config.Step}, baseline {config.Base}");
        Console.WriteLine($"  Then       {config.ConfirmSeconds} s confirmation at the limit, {config.SoakSeconds} s soak with {config.SoakEngine} at limit + {config.SafetyMargin}");
        Console.WriteLine($"  Estimate   ~{typical.TotalHours:F1} h if most cores settle within two coarse margins; up to {worst.TotalHours:F0} h worst case");
        Console.WriteLine();
        Console.WriteLine("  While it runs: leave the machine plugged in and alone. A too-deep margin can");
        Console.WriteLine("  reboot it; the campaign resumes by itself at the next logon.");
        Console.WriteLine();
        if (!Ask("  Start? [y/N] ", args, defaultYes: false)) { Console.WriteLine("  Cancelled."); return 0; }

        // ---- baseline ----
        var offBase = co.ReadAll().Where(r => r.Margin != config.Base).Select(r => r.Index).ToList();
        if (offBase.Count > 0)
        {
            Console.WriteLine($"  Cores {string.Join(",", offBase)} are not at the baseline {config.Base}; resetting...");
            Stepper.Apply(co, Enumerable.Range(0, co.CoreCount).Select(c => (c, config.Base)).ToList());
        }

        AppPaths.EnsureData();
        File.WriteAllText(AppPaths.CurrentCampaign, dir);

        // If a positive cold-reboots the machine, the campaign continues by itself at logon.
        var selfExe = File.Exists(AppPaths.Exe) ? AppPaths.Exe : Environment.ProcessPath!;
        if (Service.InstallFindResume(selfExe, Path.Combine(dir, "resume.log")) == 0)
            Console.WriteLine("  Auto-resume armed: if the machine reboots, the campaign continues at logon (task rycolab-find-resume).");
        else
            Console.Error.WriteLine("  Could not create the auto-resume task; after a reboot run `rycolab find --resume` by hand.");

        // ---- sweep ----
        var options = new SweepOptions { Cores = cores, CampaignDir = dir, Quick = quick };
        using var telemetry = new Telemetry();
        var pm = new PmTable(co.Cpu);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        int code;
        Dictionary<int, int?> limits;
        if (args.Has("plain"))
        {
            var sweep = new Sweep(co, config, options, telemetry, pm, new PlainSweepSink());
            code = sweep.Run(cts.Token);
            limits = sweep.Limits;
        }
        else
        {
            var view = new SweepView(cores, config.Seconds, known);
            var result = 0;
            Dictionary<int, int?> lim = [];
            AnsiConsole.Live(view.Render()).AutoClear(false).Start(ctx =>
            {
                view.Changed = () => { ctx.UpdateTarget(view.Render()); ctx.Refresh(); };
                var sweep = new Sweep(co, config, options, telemetry, pm, view);
                result = sweep.Run(cts.Token);
                lim = sweep.Limits;
            });
            code = result;
            limits = lim;
        }

        if (code != 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Interrupted. `rycolab find --resume` continues where it stopped.");
            return code;
        }

        Service.RemoveFindResume();

        // ---- proposal ----
        var profile = Profile.FromLimits(limits, config, Path.GetFileName(dir.TrimEnd('\\', '/')), CpuFingerprint.Of(co));
        Console.WriteLine();
        Console.WriteLine("  Limits found (first clean margin per core):");
        foreach (var line in CoreRows.Lines(coreCount, c => $"{c}:{Fmt(limits, c)}")) Console.WriteLine($"    {line}");
        Console.WriteLine($"  Proposed profile (limit + {config.SafetyMargin}; cores without a limit stay at {config.Base}):");
        foreach (var line in CoreRows.Lines(coreCount, c => $"{c}:{profile.Cores[c]}")) Console.WriteLine($"    {line}");
        Console.WriteLine();

        var partial = cores.Length < coreCount;
        if (partial) Console.WriteLine("  This was a partial sweep; the profile covers only the swept cores. Not saved unless --accept.");

        var accept = args.Has("accept") || (!partial && Ask("  Save it as your profile? [Y/n] ", args, defaultYes: true));
        if (accept)
        {
            profile.Save();
            Console.WriteLine($"  Saved to {AppPaths.Profile}. Apply it with `rycolab on`.");
        }
        else Console.WriteLine($"  Not saved. `rycolab profile from-sweep {Path.GetFileName(dir.TrimEnd('\\', '/'))}` saves it later; `rycolab report` shows the details.");
        Console.WriteLine();
        return 0;
    }

    /// <summary>--engines zn5,p4p overrides the config's engine list for this run. Null when ok.</summary>
    internal static string? ParseEngines(Args args, Plan config)
    {
        if (args.Get("engines") is not { } spec) return null;
        var engines = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          .Select(Rycolab.Core.Engines.YCruncherBinaries.Resolve).ToArray();
        if (engines.Length == 0) return "  --engines: at least one engine (zn5, p4p, zn2 or a binary name).";
        config.Engines = engines;
        return null;
    }

    private static string Fmt(Dictionary<int, int?> limits, int c)
        => limits.TryGetValue(c, out var l) ? (l?.ToString() ?? "none") : "-";

    private static bool Ask(string prompt, Args args, bool defaultYes)
    {
        if (args.Has("yes")) return true;
        if (Console.IsInputRedirected) return defaultYes && args.Has("yes");
        Console.Write(prompt);
        var a = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(a)) return defaultYes;
        return a is "y" or "yes";
    }
}
