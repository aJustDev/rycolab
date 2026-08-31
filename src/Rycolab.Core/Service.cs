using System.Diagnostics;

namespace Rycolab.Core;

/// <summary>
/// The scheduled task that runs the guard hidden at logon. There is no
/// Windows service on purpose: the SMU needs an elevated interactive session
/// and the sleep/resume events arrive there.
/// </summary>
public static class Service
{
    public const string TaskName = "rycolab-guard";

    public static bool Exists() => Run($"/Query /TN {TaskName}", quiet: true) == 0;

    /// <summary>Creates (or replaces) the task, disabled: `on` enables it.</summary>
    public static int Install(string exe)
    {
        // No window: hidden powershell and guard in plain mode. `rycolab status` is the window.
        var tr = $"powershell -NoProfile -WindowStyle Hidden -Command \\\"& '{exe}' guard --plain\\\"";
        var code = Run($"/Create /TN {TaskName} /TR \"{tr}\" /SC ONLOGON /RL HIGHEST /IT /F", quiet: true);
        // schtasks.exe leaves Windows' defaults "start only on AC" and "stop when going on battery" on;
        // the guard must start and keep running on battery (schtasks cannot change these; PowerShell can).
        if (code == 0) code = RunPs($"Set-ScheduledTask -TaskName {TaskName} -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)) | Out-Null");
        if (code == 0) code = Disable();
        return code;
    }

    private static int RunPs(string command)
    {
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{command}\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    public const string FindTaskName = "rycolab-find-resume";

    /// <summary>
    /// Task that resumes an unfinished find campaign at logon: a too-deep
    /// margin can cold-reboot the machine (it happened on core 4 at -50,
    /// twice), and a harness that needs a human after that is not a harness.
    /// The campaign registers it when it starts; `find` removes it when the
    /// campaign completes. 30 s logon delay so the system settles first.
    /// </summary>
    public static int InstallFindResume(string exe, string log)
    {
        var tr = $"powershell -NoProfile -WindowStyle Hidden -Command \\\"& '{exe}' find --resume --yes --plain *>> '{log}'\\\"";
        var code = Run($"/Create /TN {FindTaskName} /TR \"{tr}\" /SC ONLOGON /DELAY 0000:30 /RL HIGHEST /IT /F", quiet: true);
        if (code == 0) code = RunPs($"Set-ScheduledTask -TaskName {FindTaskName} -Settings (New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero)) | Out-Null");
        return code;
    }

    public static int RemoveFindResume() => Run($"/Delete /TN {FindTaskName} /F", quiet: true);
    public static bool FindResumeExists() => Run($"/Query /TN {FindTaskName}", quiet: true) == 0;
    public static int StartFindResume() => Run($"/Run /TN {FindTaskName}", quiet: true);

    public static int Remove() => Run($"/Delete /TN {TaskName} /F", quiet: true);
    public static int Enable() => Run($"/Change /TN {TaskName} /ENABLE", quiet: true);
    public static int Disable() => Run($"/Change /TN {TaskName} /DISABLE", quiet: true);
    public static int Start() => Run($"/Run /TN {TaskName}", quiet: true);
    public static int Query() => Run($"/Query /TN {TaskName} /V /FO LIST", quiet: false);

    public static Process? GuardProcess()
        => Process.GetProcessesByName("rycolab").FirstOrDefault(p => p.Id != Environment.ProcessId);

    /// <summary>Asks the running guard to exit cleanly (it restores the baseline) and waits for it.</summary>
    public static bool Stop(int timeoutSeconds = 90)
    {
        var others = Process.GetProcessesByName("rycolab").Where(p => p.Id != Environment.ProcessId).ToList();
        if (others.Count == 0) return true;
        Directory.CreateDirectory(AppPaths.Guard);
        File.WriteAllText(Guard.StopFile(AppPaths.Guard), DateTime.Now.ToString("o"));
        foreach (var p in others) p.WaitForExit(timeoutSeconds * 1000);
        return GuardProcess() is null;
    }

    private static int Run(string arguments, bool quiet)
    {
        var psi = new ProcessStartInfo("schtasks.exe", arguments) { UseShellExecute = false, RedirectStandardOutput = quiet, RedirectStandardError = quiet };
        var p = Process.Start(psi)!;
        if (quiet) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); }
        p.WaitForExit();
        return p.ExitCode;
    }
}
