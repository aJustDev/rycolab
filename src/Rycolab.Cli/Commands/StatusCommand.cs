using System.Text.Json;
using Rycolab.Cli.Ui;
using Rycolab.Core;
using Profile = Rycolab.Core.Profile;
using Spectre.Console;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab status [--follow] [--json]: what the guard is doing, read from
/// state.json without elevation. --follow redraws the guard panel as the
/// state changes; Ctrl+C only closes the panel.
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

        Console.WriteLine();
        Console.WriteLine(guard is { } g
            ? $"  guard              RUNNING (pid {g.Id}, since {g.StartTime:HH:mm:ss})"
            : "  guard              not running");
        Console.WriteLine($"  profile            {(profile is null ? "none" : Describe(profile))}");

        if (state is not null)
        {
            Console.WriteLine($"  phase              {state.Phase}{(state.ValidationStartedAt is { } v ? $"  (validation since {v:yyyy-MM-dd}, {state.GuardedSeconds / 3600.0:F1} h guarded, {state.Resumes} resumes, {state.Reapplies} re-applies, {state.Whea} WHEA, {state.Resets} resets)" : "")}");
            if (state.LastTick is { } t)
                Console.WriteLine($"  last sample        {t:yyyy-MM-dd HH:mm:ss}  {state.LastState}  CPU {state.CpuLoad?.ToString("F0") ?? "-"} %  package {state.PackagePower?.ToString("F1") ?? "-"} W");
            var count = state.Hardware?.Length is > 0 and var n ? n : profile?.Fingerprint?.Cores is > 0 and var f ? f : Topology.MaxCores;
            if (state.Hardware is { } hw)
                Console.WriteLine($"  hardware           {string.Join("   ", CoreRows.Lines(count, c => c < hw.Length ? hw[c]?.ToString() ?? "-" : "-", " "))}{(guard is null ? "  (last seen by the guard)" : "")}");
            if (profile is not null)
                Console.WriteLine($"  profile            {string.Join("   ", CoreRows.Lines(count, c => profile.Cores[c].ToString(), " "))}");
            if (state.PowerProfile is { } pp) Console.WriteLine($"  power auto         {pp} profile applied by the guard");
            if (state.LastError is not null) Console.WriteLine($"  last error         {state.LastError}");
            if (state.LastEvents.Count > 0)
            {
                Console.WriteLine("  events");
                foreach (var e in state.LastEvents.TakeLast(8)) Console.WriteLine($"    {e}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(guard is not null && state is { Applied: true }
            ? "  The profile is applied and guarded."
            : guard is null
                ? $"  No guard: the cores are at the BIOS baseline{(profile is null ? "" : "; `rycolab on` applies the profile")}."
                : "  The guard is running but the profile is not applied right now (see events).");
        Console.WriteLine();
        return 0;
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
            DateTime? shown = null;
            while (!cts.IsCancellationRequested)
            {
                var state = State.Load();
                var alive = Service.GuardProcess() is not null;
                if (state is not null)
                {
                    view.Set(state);
                    if (state.LastTick != shown) shown = state.LastTick;
                }
                if (!alive) view.OnEventOnce("(guard is not running; this panel only reads state.json)");
                ctx.UpdateTarget(view.Render());
                ctx.Refresh();
                Thread.Sleep(1000);
            }
        });
        return 0;
    }
}
