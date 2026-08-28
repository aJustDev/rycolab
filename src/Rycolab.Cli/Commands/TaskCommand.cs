using System.Diagnostics;
using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// Scheduled task that starts guard at logon, elevated, hidden.
/// rycolab task install|run|stop|remove|status [--plan path]
/// </summary>
public static class TaskCommand
{
    public const string TaskName = "rycolab-guard";

    public static int Run(Args args)
    {
        var sub = args.Positional.FirstOrDefault() ?? "status";
        switch (sub)
        {
            case "install":
            {
                var plan = Path.GetFullPath(args.Get("plan") ?? Plan.DefaultPath);
                if (!File.Exists(plan)) { Console.Error.WriteLine($"Plan not found: {plan}"); return 1; }
                var exe = Environment.ProcessPath!;
                // No window: hidden powershell and guard in plain mode. 'rycolab status' is the window.
                var tr = $"powershell -NoProfile -WindowStyle Hidden -Command \\\"& '{exe}' guard --plain --plan '{plan}'\\\"";
                var code = Schtasks($"/Create /TN {TaskName} /TR \"{tr}\" /SC ONLOGON /RL HIGHEST /IT /F");
                if (code == 0) Console.WriteLine($"  Task {TaskName} created: hidden guard with {plan} at logon. 'rycolab status' shows it.");
                return code;
            }
            case "remove":
                return Schtasks($"/Delete /TN {TaskName} /F");
            case "run":
            {
                if (Process.GetProcessesByName("rycolab").Any(p => p.Id != Environment.ProcessId))
                {
                    Console.Error.WriteLine("Another rycolab is already running (guard or sweep). Stop it first.");
                    return 1;
                }
                var code = Schtasks($"/Run /TN {TaskName}");
                if (code == 0) Console.WriteLine("  guard started hidden through the task. 'rycolab status' shows it; 'rycolab task stop' stops it.");
                return code;
            }
            case "stop":
            {
                var runs = new GuardOptions().RunsDir;
                var others = Process.GetProcessesByName("rycolab").Where(p => p.Id != Environment.ProcessId).ToList();
                if (others.Count == 0) { Console.WriteLine("  No guard is running."); return 0; }
                File.WriteAllText(Guard.StopFile(runs), DateTime.Now.ToString("o"));
                Console.WriteLine("  Stop requested; guard sees it on its next sample (up to 1 min) and restores the baseline.");
                foreach (var p in others) p.WaitForExit(90_000);
                var still = Process.GetProcessesByName("rycolab").Any(p => p.Id != Environment.ProcessId);
                Console.WriteLine(still ? "  Still alive; check its window." : "  guard stopped.");
                return still ? 1 : 0;
            }
            case "status":
                return Schtasks($"/Query /TN {TaskName} /V /FO LIST");
            default:
                Console.Error.WriteLine($"Unknown subcommand: {sub}");
                return 2;
        }
    }

    private static int Schtasks(string arguments)
    {
        var p = Process.Start(new ProcessStartInfo("schtasks.exe", arguments) { UseShellExecute = false })!;
        p.WaitForExit();
        return p.ExitCode;
    }
}
