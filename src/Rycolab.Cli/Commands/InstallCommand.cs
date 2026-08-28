using Rycolab.Core;
using Rycolab.Core.Engines;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab install [--ycruncher DIR] [--no-task] [--base N]
/// Binaries to %LOCALAPPDATA%\rycolab\bin, user PATH, y-cruncher (download or
/// copy), baseline read from the hardware into config.json, scheduled task
/// (disabled until `on`). Idempotent.
/// </summary>
public static class InstallCommand
{
    public static int Run(Args args)
    {
        void Log(string s) => Console.WriteLine($"  {s}");
        Console.WriteLine();

        if (Service.GuardProcess() is { } g)
        {
            Console.Error.WriteLine($"  A guard is running (pid {g.Id}). Run `rycolab off` first, so the baseline can be read.");
            return 2;
        }

        AppPaths.EnsureData();
        var copied = Installer.CopyBinaries(Log);
        Installer.AddToUserPath(Log);

        var config = Plan.LoadOrDefault();
        Log($"y-cruncher engines for this CPU: {string.Join(" | ", config.Engines)} ({YCruncherBinaries.Why()})");
        if (args.Get("ycruncher") is { } dir)
            Installer.CopyYCruncher(Environment.ExpandEnvironmentVariables(dir), Log, config.Engines);
        else if (Installer.HasYCruncher(engines: config.Engines))
            Log("y-cruncher already present");
        else
        {
            try { Installer.DownloadYCruncher(Log, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  y-cruncher download failed: {ex.Message}");
                Console.Error.WriteLine($"  Get {Installer.YCruncherUrl} yourself and run: rycolab install --ycruncher <folder with Binaries>");
                return 1;
            }
        }

        using (var co = new CoController())
        {
            Log($"CPU {co.CpuName}, {co.CoreCount} cores, SMU {co.SmuType}, per-core Curve Optimizer: {(co.IsPsmSupported ? "supported" : "NOT SUPPORTED")}");
            if (!co.IsPsmSupported)
            {
                Console.Error.WriteLine("  This CPU's SMU does not expose SetDldoPsmMargin: per-core Curve Optimizer is not available here.");
                return 1;
            }
            var baseline = args.GetInt("base") ?? (File.Exists(AppPaths.Config) ? config.Base : Installer.ReadBaseline(co, Log));
            config.Base = baseline;
            config.Save();
            Log($"config {AppPaths.Config}: baseline {baseline}, engines {string.Join(" | ", config.Engines)}, tests {string.Join(",", config.Tests)}, {config.Seconds} s per run");
        }

        if (!args.Has("no-task"))
        {
            var exe = File.Exists(AppPaths.Exe) ? AppPaths.Exe : Environment.ProcessPath!;
            if (Service.Install(exe) == 0) Log($"scheduled task {Service.TaskName} created (disabled until `rycolab on`)");
            else Console.Error.WriteLine("  could not create the scheduled task (schtasks failed).");
        }

        Console.WriteLine();
        Console.WriteLine(copied
            ? $"  Installed. Open a new console and run `rycolab`{(Profile.Exists() ? "" : "; there is no profile yet: `rycolab sweep` finds it")}."
            : "  Up to date.");
        Console.WriteLine();
        return 0;
    }
}
