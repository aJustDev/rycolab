using System.Text.Json;
using Rycolab.Cli.Ui;
using Rycolab.Core;
using Profile = Rycolab.Core.Profile;
using Spectre.Console;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab status [--once] [--follow] [--json]: everything applied right now
/// in one panel - Curve Optimizer (guard, phase, per-core), battery profile,
/// Lenovo EC (elevated only; degrades to a hint) and the Windows scheme.
/// By default the panel stays up refreshing every 2 s until Ctrl+C; --once
/// prints it and exits. --follow is the old per-core guard view.
/// </summary>
public static class StatusCommand
{
    public static int Run(Args args)
    {
        var profile = Profile.Exists() ? Profile.Load() : null;
        var state = State.Load();
        var guard = Service.GuardProcess();

        if (args.Has("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(new { guardRunning = guard is not null, guardPid = guard?.Id, profile, state }, Profile.Json));
            return 0;
        }

        if (args.Has("once"))
        {
            WritePanel(profile, state, guard);
            Console.WriteLine($"  Next: {Next(profile, state, guard)}");
            Console.WriteLine();
            return 0;
        }

        return Live(profile);
    }

    /// <summary>The four sections, once. Shared with the bare `rycolab` command.</summary>
    public static void WritePanel(Profile? profile, State? state, System.Diagnostics.Process? guard)
    {
        // The cpu-top row measures between two samples; give the one-shot render a real window.
        ProcessLoad.Top(0);
        Thread.Sleep(600);
        var elevated = Elevation.IsElevated();
        using var ec = elevated ? new LenovoEc() : null;
        using var energy = elevated ? new LenovoEnergy() : null;
        Console.WriteLine();
        AnsiConsole.Write(new Rows(
            StatusView.Co(guard, state, profile),
            StatusView.Battery(state),
            StatusView.Ec(ec, energy),
            StatusView.Windows()));
        Console.WriteLine();
    }

    /// <summary>
    /// Default mode: the panel redrawn every 2 s until Ctrl+C. The cheap
    /// sources (state.json, battery, EC) refresh every cycle; powercfg (five
    /// child processes) and the charge-mode driver every eighth (~16 s).
    /// </summary>
    private static int Live(Profile? profile)
    {
        var elevated = Elevation.IsElevated();
        using var ec = elevated ? new LenovoEc() : null;
        using var energy = elevated ? new LenovoEnergy() : null;
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        Spectre.Console.Rendering.IRenderable windows = StatusView.Windows();
        var tick = 0;
        AnsiConsole.Live(new Markup("[grey]reading...[/]")).AutoClear(false).Start(ctx =>
        {
            while (!cts.IsCancellationRequested)
            {
                var state = State.Load();
                var guard = Service.GuardProcess();
                if (tick > 0 && tick % 8 == 0) windows = StatusView.Windows();
                tick++;
                ctx.UpdateTarget(new Rows(
                    StatusView.Co(guard, state, profile),
                    StatusView.Battery(state),
                    StatusView.Ec(ec, energy),
                    windows,
                    new Markup($"  [bold]Next:[/] {Markup.Escape(Next(profile, state, guard))}\n  [grey]{DateTime.Now:HH:mm:ss}  refreshing every 2 s; Ctrl+C closes[/]")));
                ctx.Refresh();
                cts.Token.WaitHandle.WaitOne(2000);
            }
        });
        Console.WriteLine();
        return 0;
    }

    /// <summary>One-sentence suggestion, shared with the bare `rycolab` command.</summary>
    public static string Next(Profile? profile, State? state, System.Diagnostics.Process? guard)
    {
        if (profile is null) return "no profile yet. `rycolab find` (elevated) measures each core and proposes one; `rycolab find --quick --cores 0` is a 10-minute first look.";
        if (guard is null) return "the profile is not being applied: run `rycolab on` (elevated).";
        return state?.Phase switch
        {
            "validating" => $"profile in validation: {state.GuardedSeconds / 3600.0:F1} h guarded, {state.Resumes} resumes, {state.Whea} WHEA, {state.Resets} unexplained resets. Use the machine normally.",
            "steady" => "profile validated and applied. `rycolab off` returns to the baseline.",
            "positive" => state.Positive == "lost"
                ? "the guard gave up: something else kept overwriting the margins (Legion Toolkit's Curve Optimizer?). The baseline is applied; close the other writer, then `rycolab on`."
                : "the guard stopped on a positive (WHEA). The baseline is applied; check the events above.",
            _ => "all good; `rycolab status --follow` watches the guard live.",
        };
    }

    public static string Describe(Profile p)
    {
        var src = p.Source is { } s ? $"from {s.Campaign} ({s.Date:yyyy-MM-dd}, limit + {s.SafetyMargin})" : "NO SOURCE";
        return $"{string.Join(",", p.Cores)}  {src}";
    }

    private static int Follow(Profile profile)
    {
        var view = new GuardView(profile);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        AnsiConsole.Live(view.Render()).AutoClear(false).Start(ctx =>
        {
            while (!cts.IsCancellationRequested)
            {
                if (State.Load() is { } s)
                {
                    view.Set(s);
                    if (s.GuardPid is null) view.OnEventOnce("guard not running (state.json is the last snapshot)");
                }
                ctx.UpdateTarget(view.Render());
                ctx.Refresh();
                cts.Token.WaitHandle.WaitOne(1000);
            }
        });
        Console.WriteLine();
        return 0;
    }
}
