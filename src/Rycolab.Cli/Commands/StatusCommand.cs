using System.Text.Json;
using Rycolab.Cli.Ui;
using Rycolab.Core;
using Profile = Rycolab.Core.Profile;
using Spectre.Console;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab status [--follow] [--json]: everything applied right now in one
/// panel - Curve Optimizer (guard, phase, per-core), battery profile,
/// Lenovo EC (elevated only; degrades to a hint) and the Windows scheme.
/// --follow redraws the guard panel as the state changes; Ctrl+C only
/// closes the panel.
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

        if (args.Has("follow"))
        {
            if (profile is null) { Console.Error.WriteLine("No profile: nothing to follow."); return 1; }
            return Follow(profile);
        }

        WritePanel(profile, state, guard);
        Console.WriteLine($"  Next: {Next(profile, state, guard)}");
        Console.WriteLine();
        return 0;
    }

    /// <summary>The four sections. Shared with the bare `rycolab` command.</summary>
    public static void WritePanel(Profile? profile, State? state, System.Diagnostics.Process? guard)
    {
        Console.WriteLine();
        AnsiConsole.Write(new Rows(
            StatusView.Co(guard, state, profile),
            StatusView.Battery(state),
            StatusView.Ec(Elevation.IsElevated()),
            StatusView.Windows()));
        Console.WriteLine();
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
            "positive" => "the guard stopped on a positive (WHEA). The baseline is applied; check the events above.",
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
