using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>`rycolab` with no arguments: one screen of state and the suggested next step. No elevation.</summary>
public static class RootCommand
{
    public static int Run(Args args)
    {
        Console.WriteLine();
        var installed = File.Exists(AppPaths.Exe);
        var config = Plan.LoadOrDefault();
        var hasYc = Installer.HasYCruncher(config.YCruncherDir, config.Engines);
        var profile = Profile.Exists() ? Profile.Load() : null;
        var state = State.Load();
        var guard = Service.GuardProcess();

        Console.WriteLine($"  rycolab {(installed ? "installed in " + AppPaths.Data : "NOT installed (running from " + AppContext.BaseDirectory.TrimEnd('\\') + ")")}");
        Console.WriteLine($"  y-cruncher         {(hasYc ? "ok" : "missing")}");
        Console.WriteLine($"  profile            {(profile is null ? "none" : StatusCommand.Describe(profile))}");
        Console.WriteLine($"  guard              {(guard is null ? "not running" : $"running (pid {guard.Id})")}");
        if (state is { LastTick: not null })
            Console.WriteLine($"  last sample        {state.LastTick:yyyy-MM-dd HH:mm:ss}  {state.LastState}  WHEA {state.Whea}  phase {state.Phase}");
        Console.WriteLine();

        string next;
        if (!installed) next = $"not on the PATH yet: run `sudo \"{Environment.ProcessPath}\" install`, then open a new console.";
        else if (!hasYc) next = "run `rycolab install` again to fetch y-cruncher.";
        else if (profile is null) next = "no profile yet: run `rycolab sweep` (leave the machine alone, it can take hours), then `rycolab profile from-sweep <campaign>` and `rycolab on`.";
        else if (guard is null) next = "the profile is not being applied: run `rycolab on` (elevated).";
        else if (state?.Phase == "validating") next = $"profile in validation: {state.GuardedSeconds / 3600.0:F1} h guarded, {state.Resumes} resumes, {state.Whea} WHEA. Use the machine normally; `rycolab status` for details.";
        else if (state?.Phase == "steady") next = "profile validated and applied. `rycolab status` for details, `rycolab off` to return to the baseline.";
        else if (state?.Phase == "positive") next = "the guard stopped on a positive (WHEA). Check `rycolab status`; the baseline is applied.";
        else next = "`rycolab status` for details.";
        Console.WriteLine($"  Next: {next}");
        Console.WriteLine();
        return 0;
    }
}
