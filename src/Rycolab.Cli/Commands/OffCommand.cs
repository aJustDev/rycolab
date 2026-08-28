using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>rycolab off: stop the guard cleanly (it restores the baseline) and disable the task.</summary>
public static class OffCommand
{
    public static int Run(Args args)
    {
        Console.WriteLine();
        if (Service.Exists()) Service.Disable();
        if (Service.GuardProcess() is null)
        {
            Console.WriteLine("  No guard running; the task stays disabled until `rycolab on`.");
        }
        else
        {
            Console.WriteLine("  Asking the guard to stop (it restores the baseline on its next sample, up to 1 min)...");
            if (!Service.Stop()) { Console.Error.WriteLine("  The guard did not stop in time."); return 1; }
            Console.WriteLine("  Guard stopped.");
        }

        using var co = new CoController();
        var readings = co.ReadAll();
        var distinct = readings.Where(r => r.Margin.HasValue).Select(r => r.Margin!.Value).Distinct().OrderBy(x => x).ToList();
        Console.WriteLine($"  Hardware now: {string.Join("/", distinct)} on all cores. The profile will not be applied at logon until `rycolab on`.");
        Console.WriteLine();
        return 0;
    }
}
