using Rycolab.Cli.Ui;
using Rycolab.Core;
using Profile = Rycolab.Core.Profile;
using Spectre.Console;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab find [--quick] [--cores 0-15] [--resume] [--yes] [--accept] [--plain]
/// The sweep with a wizard around it: checks, time estimate, confirmation,
/// campaign bookkeeping, and the proposed profile at the end.
/// </summary>
public static class FindCommand
{
    private static readonly string[] QuickTests = ["SFTv4", "FFTv4", "N63"];
    private const int QuickSeconds = 180;

    public static int Run(Args args)
    {
        Console.WriteLine();
        var config = Plan.LoadOrDefault();
        var quick = args.Has("quick");
        if (quick) { config.Tests = QuickTests; config.Seconds = QuickSeconds; }

        var cores = SweepCommand.ParseCores(args.Get("cores") ?? "0-15");
        if (cores is null) { Console.Error.WriteLine("  --cores: use 0-15, 0,3,8-11 ..."); return 2; }

        // ---- checks ----
        var problems = new List<string>();
        if (!Installer.HasYCruncher(config.YCruncherDir)) problems.Add($"y-cruncher binaries missing in {config.YCruncherDir}: run `rycolab install`.");
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
        var current = File.Exists(AppPaths.CurrentCampaign) ? File.ReadAllText(AppPaths.CurrentCampaign).Trim() : null;
        var currentUnfinished = current is not null && Directory.Exists(current) && !IsComplete(current, cores);
        if (resume)
        {
            if (!currentUnfinished) { Console.Error.WriteLine("  Nothing to resume."); return 2; }
            dir = current!;
        }
        else if (currentUnfinished && !args.Has("new"))
        {
            Console.WriteLine($"  There is an unfinished campaign: {current}");
            if (File.Exists(Path.Combine(current!, "in-progress.json")))
                Console.WriteLine("  It has a run in progress: the machine hung or rebooted during it (that counts as a positive).");
            if (!Ask("  Resume it? [Y/n] ", args, defaultYes: true)) dir = AppPaths.Campaign($"find-{DateTime.Now:yyyyMMdd-HHmm}");
            else dir = current!;
        }
        else dir = AppPaths.Campaign($"find-{DateTime.Now:yyyyMMdd-HHmm}");

        var known = Journal.ReadJsonFile<Dictionary<string, int?>>(Path.Combine(dir, "limits.json"))?
                        .ToDictionary(k => int.Parse(k.Key), k => k.Value) ?? [];
        var pending = cores.Where(c => !known.ContainsKey(c)).ToArray();

        // ---- estimate ----
        var margins = (config.Top - config.Start) / config.Step + 1;
        var perRun = config.Seconds + 20;
        var typical = TimeSpan.FromSeconds((double)pending.Length * 2 * config.Engines.Length * perRun);
        var worst = TimeSpan.FromSeconds((double)pending.Length * margins * config.Engines.Length * perRun);
        Console.WriteLine($"  Campaign   {dir}");
        Console.WriteLine($"  Cores      {pending.Length} pending of {cores.Length} ({string.Join(",", pending)})");
        Console.WriteLine($"  Per run    {config.Seconds} s, engines {string.Join(" | ", config.Engines)}, tests {string.Join(",", config.Tests)}{(quick ? "  (quick)" : "")}");
        Console.WriteLine($"  Margins    {config.Start} -> {config.Top} step {config.Step}, baseline {config.Base}");
        Console.WriteLine($"  Estimate   ~{typical.TotalHours:F1} h if most cores settle within two margins; up to {worst.TotalHours:F0} h worst case");
        Console.WriteLine();
        Console.WriteLine("  While it runs: leave the machine plugged in and alone. A too-deep margin can");
        Console.WriteLine("  reboot it; if that happens, run `rycolab find` again and it resumes.");
        Console.WriteLine();
        if (!Ask("  Start? [y/N] ", args, defaultYes: false)) { Console.WriteLine("  Cancelled."); return 0; }

        // ---- baseline ----
        using var co = new CoController();
        if (!co.IsPsmSupported) { Console.Error.WriteLine("  This SMU does not support per-core Curve Optimizer."); return 1; }
        var offBase = co.ReadAll().Where(r => r.Margin != config.Base).Select(r => r.Index).ToList();
        if (offBase.Count > 0)
        {
            Console.WriteLine($"  Cores {string.Join(",", offBase)} are not at the baseline {config.Base}; resetting...");
            Stepper.Apply(co, Enumerable.Range(0, co.CoreCount).Select(c => (c, config.Base)).ToList());
        }

        AppPaths.EnsureData();
        File.WriteAllText(AppPaths.CurrentCampaign, dir);

        // ---- sweep ----
        var options = new SweepOptions { Cores = cores, CampaignDir = dir };
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

        // ---- proposal ----
        var profile = Profile.FromLimits(limits, config, Path.GetFileName(dir.TrimEnd('\\', '/')), CpuFingerprint.Of(co));
        Console.WriteLine();
        Console.WriteLine("  Limits found (first clean margin per core):");
        Console.WriteLine($"    CCD0 {string.Join("  ", Enumerable.Range(0, 8).Select(c => $"{c}:{Fmt(limits, c)}"))}");
        Console.WriteLine($"    CCD1 {string.Join("  ", Enumerable.Range(8, 8).Select(c => $"{c}:{Fmt(limits, c)}"))}");
        Console.WriteLine($"  Proposed profile (limit + {config.SafetyMargin}; cores without a limit stay at {config.Base}):");
        Console.WriteLine($"    CCD0 {string.Join("  ", profile.Cores.Take(8).Select((m, i) => $"{i}:{m}"))}");
        Console.WriteLine($"    CCD1 {string.Join("  ", profile.Cores.Skip(8).Select((m, i) => $"{i + 8}:{m}"))}");
        Console.WriteLine();

        var partial = cores.Length < Topology.MaxCores;
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

    private static bool IsComplete(string dir, int[] cores)
    {
        var limits = Journal.ReadJsonFile<Dictionary<string, int?>>(Path.Combine(dir, "limits.json"));
        return limits is not null && cores.All(c => limits.ContainsKey(c.ToString()));
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
