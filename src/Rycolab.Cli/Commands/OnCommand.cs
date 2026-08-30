using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab on: check the profile (source, CPU fingerprint, limits), enable the
/// task, start the hidden guard and wait for its first sample.
/// </summary>
public static class OnCommand
{
    public static int Run(Args args)
    {
        Console.WriteLine();
        if (!Profile.Exists())
        {
            Console.Error.WriteLine("  No profile. Run `rycolab sweep` and `rycolab profile from-sweep <campaign>` first.");
            return 2;
        }
        var profile = Profile.Load();

        using (var co = new CoController())
        {
            if (profile.RefusalReason(co) is { } why)
            {
                Console.Error.WriteLine($"  Refusing to apply the profile: {why}");
                return 2;
            }
        }
        // No AC check here: the guard keeps a validated profile and already runs on battery after the line drops.
        // The stress campaigns (find, sweep) are the ones that insist on the charger.

        if (Service.GuardProcess() is { } g)
        {
            Console.WriteLine($"  The guard is already running (pid {g.Id}).");
            return StatusCommand.Run(new Args([]));
        }

        var exe = File.Exists(AppPaths.Exe) ? AppPaths.Exe : Environment.ProcessPath!;
        if (!Service.Exists() && Service.Install(exe) != 0)
        {
            Console.Error.WriteLine("  Could not create the scheduled task.");
            return 1;
        }
        Service.Enable();
        var stamp = DateTime.Now;
        if (Service.Start() != 0)
        {
            Console.Error.WriteLine("  Could not start the guard through the task.");
            return 1;
        }

        Console.WriteLine("  Guard started. Waiting for its first sample...");
        for (var i = 0; i < 120; i++)
        {
            Thread.Sleep(1000);
            var s = State.Load();
            if (s is { LastTick: not null } && s.LastTick > stamp)
            {
                Console.WriteLine($"  Profile applied on all cores and guarded ({s.Phase}). It re-applies after sleep and at logon; `rycolab off` returns to the baseline.");
                Console.WriteLine();
                return 0;
            }
            // Only a state written by the guard we just launched counts; the previous one may hold an old error.
            if (s is { GuardPid: null, LastError: not null } && s.Since > stamp && s.LastEvents.Any(e => e.Contains("apply-failed")))
            {
                Console.Error.WriteLine($"  The guard could not apply the profile: {s.LastError}");
                return 1;
            }
            if (i > 10 && Service.GuardProcess() is null)
            {
                Console.Error.WriteLine("  The guard exited before its first sample. `rycolab status` shows its last events.");
                return 1;
            }
        }
        Console.Error.WriteLine("  No sample after 2 minutes; `rycolab status` shows what the guard is doing.");
        return 1;
    }
}
