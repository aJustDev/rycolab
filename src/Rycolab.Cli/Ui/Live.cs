using Rycolab.Core;
using Profile = Rycolab.Core.Profile;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Rycolab.Cli.Ui;

/// <summary>Guard live panel: one row per core (profile / hardware / state) and the latest events.</summary>
public sealed class GuardView
{
    private readonly Profile _profile;
    private readonly List<string> _events = [];
    private GuardTick? _last;
    private State? _state;

    public GuardView(Profile profile) => _profile = profile;

    public void OnTick(GuardTick t) => _last = t;

    /// <summary>Feed from state.json (status --follow) instead of live ticks.</summary>
    public void Set(State s)
    {
        _state = s;
        if (s.LastTick is { } t && s.Hardware is { } hw)
            _last = new GuardTick(t, (int)(t - (s.Since ?? t)).TotalSeconds, s.Applied, hw, s.Whea, s.CpuLoad, s.PackagePower, s.LastState ?? "");
        _events.Clear();
        _events.AddRange(s.LastEvents);
    }

    public void OnEvent(string line)
    {
        _events.Add(line);
        if (_events.Count > 12) _events.RemoveAt(0);
    }

    public void OnEventOnce(string line)
    {
        if (_events.Count == 0 || _events[^1] != line) OnEvent(line);
    }

    public IRenderable Render()
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]rycolab guard[/]");
        table.AddColumn("Core").AddColumn("CCD").AddColumn("Profile").AddColumn("Hardware").AddColumn("State");
        for (var c = 0; c < Topology.MaxCores; c++)
        {
            var hw = _last?.Hardware is { } h && c < h.Length ? h[c] : null;
            var want = _profile.Cores[c];
            var (mark, color) = hw is null ? ("?", "grey") : hw == want ? ("ok", "green") : ("OFF", "red");
            table.AddRow(c.ToString(), Topology.CcdName(c), want.ToString(), hw?.ToString() ?? "-", $"[{color}]{mark}[/]");
        }

        var phase = _state is { } s ? $"   phase [bold]{s.Phase}[/]   guarded {s.GuardedSeconds / 3600.0:F1} h   resumes {s.Resumes}" : "";
        var status = _last is { } t
            ? $"[bold]{t.State}[/]   t {TimeSpan.FromSeconds(Math.Max(0, t.Elapsed)):hh\\:mm\\:ss}   WHEA {(t.Whea == 0 ? "[green]0[/]" : $"[red]{t.Whea}[/]")}   CPU {t.CpuLoad?.ToString("F0") ?? "-"} %   package {t.PackagePower?.ToString("F1") ?? "-"} W   {t.Ts:HH:mm:ss}{phase}"
            : "[grey]applying the profile...[/]";

        var events = new Panel(string.Join("\n", _events.Select(Markup.Escape))) { Header = new PanelHeader("events"), Expand = true };
        return new Rows(table, new Markup(status), events);
    }
}
