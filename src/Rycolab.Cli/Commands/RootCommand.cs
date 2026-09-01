using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>`rycolab` with no arguments: install checks, the status panel and the suggested next step. No elevation.</summary>
public static class RootCommand
{
    public static int Run(Args args)
    {
        var installed = File.Exists(AppPaths.Exe);
        var config = Plan.LoadOrDefault();
        var hasYc = Installer.HasYCruncher(config.YCruncherDir, config.AllEngines);
        var profile = Rycolab.Core.Profile.Exists() ? Rycolab.Core.Profile.Load() : null;
        var state = State.Load();
        var guard = Service.GuardProcess();

        Console.WriteLine();
        Console.WriteLine($"  rycolab {(installed ? "installed in " + AppPaths.Data : "NOT installed (running from " + AppContext.BaseDirectory.TrimEnd('\\') + ")")}   y-cruncher {(hasYc ? "ok" : "MISSING")}");
        StatusCommand.WritePanel(profile, state, guard);

        string next;
        if (!installed) next = $"not on the PATH yet: from an elevated console (Run as administrator) run `\"{Environment.ProcessPath}\" install`, then open a new console.";
        else if (!hasYc) next = "run `rycolab install` again to fetch y-cruncher.";
        else next = StatusCommand.Next(profile, state, guard);
        Console.WriteLine($"  Next: {next}");
        Console.WriteLine();
        return 0;
    }
}
