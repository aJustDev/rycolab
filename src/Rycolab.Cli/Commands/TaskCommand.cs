using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>rycolab task install|run|stop|remove|status: the scheduled task by hand (`on`/`off` do this for you).</summary>
public static class TaskCommand
{
    public static int Run(Args args)
    {
        var sub = args.Positional.FirstOrDefault() ?? "status";
        switch (sub)
        {
            case "install":
            {
                var exe = File.Exists(AppPaths.Exe) ? AppPaths.Exe : Environment.ProcessPath!;
                var code = Service.Install(exe);
                Console.WriteLine(code == 0 ? $"  Task {Service.TaskName} created (disabled; `rycolab on` enables it). Runs {exe} guard --plain at logon." : "  schtasks failed.");
                return code;
            }
            case "remove":
                return Service.Remove();
            case "run":
            {
                if (Service.GuardProcess() is not null) { Console.Error.WriteLine("  A rycolab guard or sweep is already running."); return 1; }
                Service.Enable();
                var code = Service.Start();
                Console.WriteLine(code == 0 ? "  guard started hidden through the task. `rycolab status` shows it." : "  schtasks failed.");
                return code;
            }
            case "stop":
            {
                if (Service.GuardProcess() is null) { Console.WriteLine("  No guard is running."); return 0; }
                Console.WriteLine("  Stop requested; the guard sees it on its next sample (up to 1 min) and restores the baseline.");
                var ok = Service.Stop();
                Console.WriteLine(ok ? "  guard stopped." : "  Still alive after 90 s.");
                return ok ? 0 : 1;
            }
            case "status":
                return Service.Query();
            default:
                Console.Error.WriteLine($"Unknown subcommand: {sub}");
                return 2;
        }
    }
}
