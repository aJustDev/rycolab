using Rycolab.Cli.Ui;
using Rycolab.Core;
using Profile = Rycolab.Core.Profile;
using Spectre.Console;

namespace Rycolab.Cli.Commands;

/// <summary>rycolab guard [--profile path] [--minutes N] [--interval s] [--plain]  (what the task runs, hidden, with --plain)</summary>
public static class GuardCommand
{
    public static int Run(Args args)
    {
        var custom = args.Get("profile");
        var profile = Profile.Load(custom);
        var options = new GuardOptions
        {
            Minutes = args.GetInt("minutes"),
            IntervalSeconds = args.GetInt("interval") ?? 60,
            PublishState = custom is null,
        };

        using var co = new CoController();
        if (!co.IsPsmSupported)
        {
            Console.Error.WriteLine("This SMU does not support SetDldoPsmMargin.");
            return 1;
        }
        if (custom is null && profile.RefusalReason(co) is { } why && !args.Has("force"))
        {
            Console.Error.WriteLine($"Refusing to apply the profile: {why}");
            return 2;
        }

        using var telemetry = new Telemetry();
        var pm = new PmTable(co.Cpu);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        int code;
        if (args.Has("plain"))
        {
            var guard = new Guard(co, profile, options, telemetry.IsAvailable ? telemetry : null,
                t => Console.WriteLine($"{t.Ts:HH:mm:ss}  {t.Elapsed / 60,4} min  {(t.Ok ? "ok" : "OFF PROFILE")}  WHEA {t.Whea}  CPU {t.CpuLoad?.ToString("F0") ?? "-"}%  {t.State}"),
                Console.WriteLine, pm);
            code = guard.Run(cts.Token);
        }
        else
        {
            var view = new GuardView(profile);
            var c = 0;
            AnsiConsole.Live(view.Render()).AutoClear(false).Start(ctx =>
            {
                void Refresh() { ctx.UpdateTarget(view.Render()); ctx.Refresh(); }
                var guard = new Guard(co, profile, options, telemetry.IsAvailable ? telemetry : null,
                    t => { view.OnTick(t); Refresh(); },
                    e => { view.OnEvent(e); Refresh(); }, pm);
                c = guard.Run(cts.Token);
            });
            code = c;
        }

        // Exit immediately: journal, SQLite and the baseline are already
        // handled inside Run's finally, and both a stray foreground thread
        // and a hung LibreHardwareMonitor Dispose have kept this process
        // alive after a clean stop (three times on 2026-09-01). The OS
        // releases the handles; nothing here needs a polite dispose.
        Environment.Exit(code);
        return code;
    }
}
