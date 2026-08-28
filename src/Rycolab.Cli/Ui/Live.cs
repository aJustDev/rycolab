using Rycolab.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Rycolab.Cli.Ui;

/// <summary>Guard live panel: one row per core (plan / hardware / state) and the latest events.</summary>
public sealed class GuardView
{
    private readonly Plan _plan;
    private readonly List<string> _events = [];
    private GuardTick? _last;

    public GuardView(Plan plan) => _plan = plan;

    public void OnTick(GuardTick t) => _last = t;

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
        table.AddColumn("Core").AddColumn("CCD").AddColumn("Plan").AddColumn("Hardware").AddColumn("State");
        for (var c = 0; c < Topology.MaxCores; c++)
        {
            var hw = _last?.Hardware is { } h && c < h.Length ? h[c] : null;
            var plan = _plan.Profile[c];
            var (mark, color) = hw is null ? ("?", "grey") : hw == plan ? ("ok", "green") : ("OFF", "red");
            table.AddRow(c.ToString(), Topology.CcdName(c), plan.ToString(), hw?.ToString() ?? "-", $"[{color}]{mark}[/]");
        }

        var status = _last is { } t
            ? $"[bold]{t.State}[/]   t {TimeSpan.FromSeconds(t.Elapsed):hh\\:mm\\:ss}   WHEA {(t.Whea == 0 ? "[green]0[/]" : $"[red]{t.Whea}[/]")}   CPU {t.CpuLoad?.ToString("F0") ?? "-"} %   package {t.PackagePower?.ToString("F1") ?? "-"} W   {t.Ts:HH:mm:ss}"
            : "[grey]applying the plan...[/]";

        var events = new Panel(string.Join("\n", _events.Select(Markup.Escape))) { Header = new PanelHeader("events"), Expand = true };
        return new Rows(table, new Markup(status), events);
    }
}
