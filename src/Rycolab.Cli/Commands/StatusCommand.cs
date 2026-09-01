using System.Text.Json;
using Rycolab.Cli.Ui;
using Rycolab.Core;
using Rycolab.Core.Legion;
using Profile = Rycolab.Core.Profile;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab status [--once] [--all] [--follow] [--json]: is the profile on
/// the cores right now, and in what phase. One verdict line and a few rows;
/// `--all` adds the Lenovo EC (elevated only; degrades to a hint), the
/// processes burning the CPU and the Windows scheme. By default the panel
/// stays up refreshing every 2 s until Ctrl+C; --once prints it and exits;
/// --follow is the per-core guard view fed from state.json.
/// </summary>
public static class StatusCommand
{
    public static int Run(Args args)
    {
        var profile = Profile.Exists() ? Profile.Load() : null;
        var state = State.Load();
        var guard = Service.GuardProcess();
        var all = args.Has("all");

        if (args.Has("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(new { guardRunning = guard is not null, guardPid = guard?.Id, profile, state }, Profile.Json));
            return 0;
        }

        if (args.Has("follow"))
        {
            if (profile is null) { Console.Error.WriteLine("  No profile to follow."); return 2; }
            return Follow(profile);
        }

        if (args.Has("once"))
        {
            WritePanel(profile, state, guard, all);
            Console.WriteLine($"  Next: {Next(profile, state, guard)}");
            Console.WriteLine();
            return 0;
        }

        return Live(profile, all);
    }

    /// <summary>The summary once, plus the `--all` panels. Shared with the bare `rycolab` command.</summary>
    public static void WritePanel(Profile? profile, State? state, System.Diagnostics.Process? guard, bool all = false)
    {
        Console.WriteLine();
        if (!all)
        {
            AnsiConsole.Write(StatusView.Summary(guard, state, profile));
            Console.WriteLine();
            return;
        }
        // The cpu-top row measures between two samples; give the one-shot render a real window.
        ProcessLoad.Top(0);
        Thread.Sleep(600);
        var elevated = Elevation.IsElevated();
        using var ec = elevated ? new LenovoEc() : null;
        using var energy = elevated ? new LenovoEnergy() : null;
        AnsiConsole.Write(new Rows(
            StatusView.Summary(guard, state, profile),
            StatusView.Machine(state),
            StatusView.Ec(ec, energy),
            StatusView.Windows()));
        Console.WriteLine();
    }

    /// <summary>
    /// Default mode: the panel redrawn every 2 s until Ctrl+C. With `--all`
    /// the cheap sources (state.json, battery, EC) refresh every cycle and
    /// powercfg (five child processes) every eighth (~16 s).
    /// </summary>
    private static int Live(Profile? profile, bool all)
    {
        var elevated = Elevation.IsElevated();
        using var ec = all && elevated ? new LenovoEc() : null;
        using var energy = all && elevated ? new LenovoEnergy() : null;
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        IRenderable windows = all ? StatusView.Windows() : new Markup("");
        var tick = 0;
        AnsiConsole.Live(new Markup("[grey]reading...[/]")).AutoClear(false).Start(ctx =>
        {
            while (!cts.IsCancellationRequested)
            {
                var state = State.Load();
                var guard = Service.GuardProcess();
                if (all && tick > 0 && tick % 8 == 0) windows = StatusView.Windows();
                tick++;
                var rows = new List<IRenderable> { StatusView.Summary(guard, state, profile) };
                if (all) rows.AddRange([StatusView.Machine(state), StatusView.Ec(ec, energy), windows]);
                rows.Add(new Markup($"  [bold]Next:[/] {Markup.Escape(Next(profile, state, guard))}\n  [grey]refreshing every 2 s; Ctrl+C closes{(all ? "" : "; --all adds the machine, the Lenovo EC and the Windows scheme")}[/]"));
                ctx.UpdateTarget(new Rows(rows));
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
        if (guard is null) return state?.Phase == "positive"
            ? Positive(state)
            : "the profile is not being applied: run `rycolab on` (elevated).";
        return state?.Phase switch
        {
            "validating" => $"profile in validation: {state.GuardedSeconds / 3600.0:F1} h guarded, {state.Resumes} resumes, {state.Whea} WHEA, {state.Resets} unexplained resets. Use the machine normally.",
            "steady" => "profile validated and applied. `rycolab off` returns to the baseline.",
            "positive" => Positive(state),
            _ => "all good; `rycolab status --follow` watches the guard live.",
        };
    }

    private static string Positive(State state) => state.Positive == "lost"
        ? "the guard gave up: something else kept overwriting the margins (Legion Toolkit's Curve Optimizer?). The baseline is applied; close the other writer, then `rycolab on`."
        : "the guard stopped on a positive (WHEA). The baseline is applied; check the events above.";

    public static string Describe(Profile p)
    {
        var src = p.Source is { } s ? $"{s.Campaign}, limit + {s.SafetyMargin}" : "NO SOURCE";
        return $"{string.Join(",", p.Cores)}   {src}";
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
