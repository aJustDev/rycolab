using LegionCoLab.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace LegionCoLab.Cli.Ui;

/// <summary>Panel en vivo de guard: 16 filas (plan / hardware / estado) y los ultimos eventos.</summary>
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

    public IRenderable Render()
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]colab guard[/]");
        table.AddColumn("Nucleo").AddColumn("CCD").AddColumn("Plan").AddColumn("Hardware").AddColumn("Estado");
        for (var c = 0; c < Topology.MaxCores; c++)
        {
            var hw = _last?.Hardware is { } h && c < h.Length ? h[c] : null;
            var plan = _plan.Profile[c];
            var (mark, color) = hw is null ? ("?", "grey") : hw == plan ? ("ok", "green") : ("FUERA", "red");
            table.AddRow(c.ToString(), Topology.CcdName(c), plan.ToString(), hw?.ToString() ?? "-", $"[{color}]{mark}[/]");
        }

        var status = _last is { } t
            ? $"[bold]{t.State}[/]   t {TimeSpan.FromSeconds(t.Elapsed):hh\\:mm\\:ss}   WHEA {(t.Whea == 0 ? "[green]0[/]" : $"[red]{t.Whea}[/]")}   CPU {t.CpuLoad?.ToString("F0") ?? "-"} %   paquete {t.PackagePower?.ToString("F1") ?? "-"} W   {t.Ts:HH:mm:ss}"
            : "[grey]aplicando el plan...[/]";

        var events = new Panel(string.Join("\n", _events.Select(Markup.Escape))) { Header = new PanelHeader("eventos"), Expand = true };
        return new Rows(table, new Markup(status), events);
    }
}
