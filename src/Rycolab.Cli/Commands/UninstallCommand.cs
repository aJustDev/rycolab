using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>rycolab uninstall [--purge]: stop the guard, remove the task, the PATH entry and the binaries; --purge also the data.</summary>
public static class UninstallCommand
{
    public static int Run(Args args)
    {
        Console.WriteLine();
        if (Service.GuardProcess() is not null)
        {
            Console.WriteLine("  stopping the guard (it restores the baseline)...");
            if (!Service.Stop()) { Console.Error.WriteLine("  the guard did not stop; aborting."); return 1; }
        }
        if (Service.Exists()) { Service.Remove(); Console.WriteLine($"  task {Service.TaskName} removed"); }
        Installer.RemoveFromUserPath();
        Console.WriteLine("  PATH entry removed");
        Notifier.Unregister();

        var runningFromInstall = AppContext.BaseDirectory.StartsWith(AppPaths.Bin, StringComparison.OrdinalIgnoreCase);
        if (args.Has("purge"))
        {
            // The kernel driver ZenStates needs. Shared with other tools, so only on request and only on --purge.
            if (Installer.InpoutServiceExists())
            {
                var yes = args.Has("yes");
                if (!yes && !Console.IsInputRedirected)
                {
                    Console.Write("  Remove the inpoutx64 kernel driver service too? Other tools (ZenTimings, SMUDebugTool) use it. [y/N] ");
                    yes = Console.ReadLine()?.Trim().ToLowerInvariant() is "y" or "yes";
                }
                if (yes) Installer.RemoveInpoutService(s => Console.WriteLine($"  {s}"));
                else Console.WriteLine("  inpoutx64 service kept (sc stop inpoutx64 / sc delete inpoutx64 removes it by hand).");
            }
            if (runningFromInstall)
                Console.WriteLine($"  running from {AppPaths.Bin}: delete {AppPaths.Data} by hand after this console closes.");
            else if (Directory.Exists(AppPaths.Data))
            {
                Directory.Delete(AppPaths.Data, recursive: true);
                Console.WriteLine($"  {AppPaths.Data} deleted");
            }
        }
        else if (Directory.Exists(AppPaths.Bin))
        {
            if (runningFromInstall) Console.WriteLine($"  running from {AppPaths.Bin}: delete that folder by hand after this console closes.");
            else { Directory.Delete(AppPaths.Bin, recursive: true); Console.WriteLine($"  {AppPaths.Bin} deleted (data kept in {AppPaths.Data}; --purge removes it)"); }
        }
        Console.WriteLine();
        return 0;
    }
}
