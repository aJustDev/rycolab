using LegionCoLab.Core;
using LegionCoLab.Core.Engines;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace LegionCoLab.Cli.Ui;

/// <summary>Panel del barrido: limites por nucleo, prueba en curso con telemetria, eventos.</summary>
public sealed class SweepView : ISweepSink
{
    private readonly Plan _plan;
    private readonly int[] _cores;
    private readonly int _seconds;
    private readonly Dictionary<int, int?> _limits = new();
    private readonly Dictionary<int, List<string>> _history = new();
    private readonly List<string> _events = [];
    private (int Core, int Margin, string Engine)? _current;
    private Sample? _sample;
    private EngineStatus? _status;

    public Action? Changed { get; set; }

    public SweepView(Plan plan, int[] cores, int seconds, Dictionary<int, int?> known)
    {
        _plan = plan; _cores = cores; _seconds = seconds;
        foreach (var (k, v) in known) _limits[k] = v;
    }

    public void RunStarted(int core, int margin, string engine) { _current = (core, margin, engine); _sample = null; _status = null; Changed?.Invoke(); }

    public void Progress(Sample sample, EngineStatus status) { _sample = sample; _status = status; Changed?.Invoke(); }

    public void RunEnded(RunResult r)
    {
        if (!_history.TryGetValue(r.Core, out var h)) _history[r.Core] = h = [];
        h.Add($"{r.Margin}/{Short(r.Engine)}:{Mark(r.Verdict)}");
        var tele = r.Telemetry is { } t ? $"  GHz {t.FreqMedian:F3}  V {t.VoltMedian:F4}  W {t.PowerMedian:F2}" : "";
        Event($"nucleo {r.Core}  {r.Margin}  {r.Engine}: {r.Verdict.ToUpperInvariant()} a los {r.Seconds} s{tele}{(r.Error is null ? "" : "  " + Truncate(r.Error, 80))}");
        Changed?.Invoke();
    }

    public void CoreDone(int core, int? limit) { _limits[core] = limit; Event($"nucleo {core}: limite {(limit?.ToString() ?? "ninguno hasta el tope")}"); Changed?.Invoke(); }

    public void Event(string line)
    {
        _events.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        if (_events.Count > 10) _events.RemoveAt(0);
        Changed?.Invoke();
    }

    public IRenderable Render()
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]colab sweep[/]");
        table.AddColumn("Nucleo").AddColumn("CCD").AddColumn("Limite").AddColumn("Historial");
        foreach (var c in _cores)
        {
            var lim = _limits.TryGetValue(c, out var l) ? (l?.ToString() ?? "ninguno") : (_current?.Core == c ? "[yellow]en curso[/]" : "[grey]pendiente[/]");
            var hist = _history.TryGetValue(c, out var h) ? string.Join(" ", h) : "";
            table.AddRow(c.ToString(), Topology.CcdName(c), lim, Markup.Escape(hist));
        }

        string current;
        if (_current is { } cur)
        {
            var el = _sample?.Elapsed ?? 0;
            var bar = new string('#', Math.Min(30, el * 30 / Math.Max(1, _seconds))).PadRight(30, '.');
            var s = _sample;
            current = $"[bold]nucleo {cur.Core}  margen {cur.Margin}  {Markup.Escape(cur.Engine)}[/]   {Markup.Escape($"[{bar}]")} {el}/{_seconds} s\n" +
                      $"GHz {F(s?.Freq, 3)}   V {F(s?.Volt, 4)}   W {F(s?.Power, 2)}   T {F(s?.Temp, 1)}   reloj {F(s?.Clock, 0)}   efectivo {F(s?.ClockEffective, 0)}   paquete {F(s?.PackagePower, 1)} W\n" +
                      $"salida {_status?.Lines ?? 0} lineas   suspensiones {_status?.Suspensions ?? 0}   {Markup.Escape(Truncate(_status?.LastLine ?? "", 90))}";
        }
        else current = "[grey]sin prueba en curso[/]";

        var events = new Panel(string.Join("\n", _events.Select(Markup.Escape))) { Header = new PanelHeader("eventos"), Expand = true };
        return new Rows(table, new Panel(new Markup(current)) { Header = new PanelHeader("prueba"), Expand = true }, events);
    }

    private static string F(double? v, int dec) => v?.ToString("F" + dec) ?? "-";
    private static string Short(string engine) => engine.Split(' ')[0];
    private static string Mark(string verdict) => verdict switch { "clean" => "ok", "error" => "ERR", "crashed" => "CRASH", "whea" => "WHEA", "hang" => "HANG", _ => verdict };
    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "...";
}

/// <summary>Sin panel: una linea por evento y por resultado.</summary>
public sealed class PlainSweepSink : ISweepSink
{
    public void RunStarted(int core, int margin, string engine) => Console.WriteLine($"{DateTime.Now:HH:mm:ss}  nucleo {core}  margen {margin}  {engine}");
    public void Progress(Sample s, EngineStatus st)
    {
        if (s.Elapsed % 30 == 0)
            Console.WriteLine($"  {s.Elapsed,4} s  GHz {s.Freq:F3}  V {s.Volt:F4}  W {s.Power:F2}  T {s.Temp:F1}  lineas {st.Lines}  susp {st.Suspensions}");
    }
    public void RunEnded(RunResult r) => Console.WriteLine($"{DateTime.Now:HH:mm:ss}    -> {r.Verdict.ToUpperInvariant()} a los {r.Seconds} s{(r.Error is null ? "" : "  " + r.Error)}");
    public void CoreDone(int core, int? limit) => Console.WriteLine($"{DateTime.Now:HH:mm:ss}  nucleo {core}: limite {(limit?.ToString() ?? "ninguno")}");
    public void Event(string line) => Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {line}");
}
