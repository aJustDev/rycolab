using LegionCoLab.Cli.Ui;
using LegionCoLab.Core;
using Spectre.Console;

namespace LegionCoLab.Cli.Commands;

/// <summary>colab guard --plan p [--minutes N] [--interval s] [--plain]</summary>
public static class GuardCommand
{
    public static int Run(Args args)
    {
        var plan = Plan.Load(args.Get("plan"));
        var options = new GuardOptions
        {
            Minutes = args.GetInt("minutes"),
            IntervalSeconds = args.GetInt("interval") ?? 60,
        };

        using var co = new CoController();
        if (!co.IsPsmSupported)
        {
            Console.Error.WriteLine("Este SMU no soporta SetDldoPsmMargin.");
            return 1;
        }

        using var telemetry = new Telemetry();
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        if (args.Has("plain"))
        {
            var guard = new Guard(co, plan, options, telemetry.IsAvailable ? telemetry : null,
                t => Console.WriteLine($"{t.Ts:HH:mm:ss}  {t.Elapsed / 60,4} min  {(t.Ok ? "ok" : "FUERA")}  WHEA {t.Whea}  CPU {t.CpuLoad?.ToString("F0") ?? "-"}%  {t.State}"),
                Console.WriteLine);
            return guard.Run(cts.Token);
        }

        var view = new GuardView(plan);
        var code = 0;
        AnsiConsole.Live(view.Render()).AutoClear(false).Start(ctx =>
        {
            void Refresh() { ctx.UpdateTarget(view.Render()); ctx.Refresh(); }
            var guard = new Guard(co, plan, options, telemetry.IsAvailable ? telemetry : null,
                t => { view.OnTick(t); Refresh(); },
                e => { view.OnEvent(e); Refresh(); });
            code = guard.Run(cts.Token);
        });
        return code;
    }
}
