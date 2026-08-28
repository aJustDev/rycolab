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
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        if (args.Has("plain"))
        {
            var guard = new Guard(co, profile, options, telemetry.IsAvailable ? telemetry : null,
                t => Console.WriteLine($"{t.Ts:HH:mm:ss}  {t.Elapsed / 60,4} min  {(t.Ok ? "ok" : "OFF PROFILE")}  WHEA {t.Whea}  CPU {t.CpuLoad?.ToString("F0") ?? "-"}%  {t.State}"),
                Console.WriteLine);
            return guard.Run(cts.Token);
        }

        var view = new GuardView(profile);
        var code = 0;
        AnsiConsole.Live(view.Render()).AutoClear(false).Start(ctx =>
        {
            void Refresh() { ctx.UpdateTarget(view.Render()); ctx.Refresh(); }
            var guard = new Guard(co, profile, options, telemetry.IsAvailable ? telemetry : null,
                t => { view.OnTick(t); Refresh(); },
                e => { view.OnEvent(e); Refresh(); });
            code = guard.Run(cts.Token);
        });
        return code;
    }
}
