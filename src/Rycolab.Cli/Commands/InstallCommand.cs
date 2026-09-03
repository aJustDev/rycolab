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
            Installer.CopyYCruncher(Environment.ExpandEnvironmentVariables(dir), Log, config.AllEngines);
        else if (Installer.HasYCruncher(engines: config.AllEngines))
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
            Log($"topology {co.Map.Describe()}");
            if (!co.IsPsmSupported)
            {
                Console.Error.WriteLine("  This CPU's SMU does not expose SetDldoPsmMargin: per-core Curve Optimizer is not available here.");
                return 1;
            }
            // The map is what every mask is built from: when it cannot be trusted, nothing is written on this CPU.
            if (co.TopologyWarning is { } tw)
            {
                Console.Error.WriteLine($"  !! {tw}. Not installing: `rycolab dev probe` shows the details; please report the CPU.");
                return 1;
            }
            var (mapProblems, mapNotes) = co.CheckMap();
            foreach (var n in mapNotes) Log($"note: {n}");
            if (mapProblems.Count > 0)
            {
                foreach (var p in mapProblems) Console.Error.WriteLine($"  !! {p}");
                Console.Error.WriteLine("  Not installing: the core map does not match what the SMU answers. Please report the CPU.");
                return 1;
            }
            if (co.LikelyLocked)
                Log("!! this looks like a mobile APU below Ryzen 9: AMD lets those read the margins but refuses every write (RyzenAdj issue #233). `rycolab dev probe --write-test` confirms it before you spend hours on `find`.");
            var baseline = args.GetInt("base") ?? (File.Exists(AppPaths.Config) ? config.Base : Installer.ReadBaseline(co, Log));
            config.Base = baseline;
            // Zen 3 stops at -30: a sweep from -50 would be refused at the first write.
            if (config.Start < Safety.MinMargin) { Log($"sweep start {config.Start} raised to {Safety.MinMargin}, the floor for this CPU ({co.CodeName})"); config.Start = Safety.MinMargin; }
            config.Save();
            Log($"config {AppPaths.Config}: baseline {baseline}, engines {string.Join(" | ", config.Engines)}, tests {string.Join(",", config.Tests)}, {config.Seconds} s per run, confirm {config.ConfirmSeconds} s, soak {config.SoakSeconds} s with {config.SoakEngine}");
        }

        if (!args.Has("no-task"))
        {
            var exe = File.Exists(AppPaths.Exe) ? AppPaths.Exe : Environment.ProcessPath!;
            if (Service.Install(exe) == 0) Log($"scheduled task {Service.TaskName} created (disabled until `rycolab on`)");
            else Console.Error.WriteLine("  could not create the scheduled task (schtasks failed).");
        }

        // Data from before the database (0.2: guard.jsonl, campaigns\*\runs.jsonl) is imported once, by hand.
        if (File.Exists(Path.Combine(AppPaths.Guard, "guard.jsonl")) && !File.Exists(AppPaths.Db))
            Log("there is data from the JSONL era: `rycolab db import` brings it into the database (once; nothing is deleted)");

        Console.WriteLine();
        Console.WriteLine(copied
            ? $"  Installed. Open a new console and run `rycolab`{(Profile.Exists() ? "" : "; there is no profile yet: `rycolab find` measures the cores and proposes one")}."
            : "  Up to date.");
        Console.WriteLine();
        return 0;
    }
}
