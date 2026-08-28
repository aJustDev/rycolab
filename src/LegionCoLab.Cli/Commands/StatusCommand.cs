using System.Diagnostics;
using System.Text.Json;
using LegionCoLab.Cli.Ui;
using LegionCoLab.Core;
using Spectre.Console;

namespace LegionCoLab.Cli.Commands;

/// <summary>
/// colab status [--follow]: guard vivo o no, ultima muestra y eventos de
/// runs/guard/guard.jsonl, y hardware frente al plan. --follow se pega al
/// registro y pinta el panel de guard en vivo sin tocar al proceso.
/// </summary>
public static class StatusCommand
{
    public static int Run(Args args)
    {
        var runs = new GuardOptions().RunsDir;
        var journal = Path.Combine(runs, "guard.jsonl");
        var plan = File.Exists(Plan.DefaultPath) ? Plan.Load() : null;

        if (args.Has("follow"))
        {
            if (plan is null) { Console.Error.WriteLine("Sin plan.json no hay panel que seguir."); return 1; }
            return Follow(journal, plan);
        }

        var guard = GuardProcess();
        Console.WriteLine();
        Console.WriteLine(guard is { } g
            ? $"  guard              EN MARCHA (pid {g.Id}, desde {g.StartTime:HH:mm:ss})"
            : "  guard              no esta en marcha");

        var (ticks, events) = ReadJournal(journal, sinceLastStart: true);
        if (ticks.Count > 0)
        {
            var t = ticks[^1];
            Console.WriteLine($"  ultima muestra     {t.Ts:HH:mm:ss}  {t.State}  WHEA {t.Whea}  CPU {t.CpuLoad?.ToString("F0") ?? "-"} %  paquete {t.PackagePower?.ToString("F1") ?? "-"} W  ({t.Elapsed / 60} min en esta sesion)");
        }
        if (events.Count > 0)
        {
            Console.WriteLine("  eventos");
            foreach (var e in events.TakeLast(8)) Console.WriteLine($"    {e}");
        }

        using var co = new CoController();
        var readings = co.ReadAll();
        Console.WriteLine();
        Console.WriteLine($"  hardware   CCD0 {string.Join(" ", readings.Take(8).Select(r => r.Margin?.ToString() ?? "-"))}   CCD1 {string.Join(" ", readings.Skip(8).Select(r => r.Margin?.ToString() ?? "-"))}");
        if (plan is not null)
        {
            var bad = plan.Mismatches(readings);
            Console.WriteLine($"  plan       CCD0 {string.Join(" ", plan.Profile.Take(8))}   CCD1 {string.Join(" ", plan.Profile.Skip(8))}");
            Console.WriteLine(bad.Count == 0
                ? "  El procesador lleva puesto el plan."
                : readings.All(r => r.Margin == plan.Base)
                    ? $"  Todo en la base {plan.Base}: sin guard, o reinicio/suspension sin reaplicar. 'colab task run' lo repone."
                    : $"  Fuera del plan: nucleos {string.Join(",", bad)}.");
        }
        Console.WriteLine();
        return 0;
    }

    private static int Follow(string journal, Plan plan)
    {
        var view = new GuardView(plan);
        var offset = 0L;
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // Arranca con lo que haya desde el ultimo start
        var (ticks, events) = ReadJournal(journal, sinceLastStart: true);
        foreach (var e in events) view.OnEvent(e);
        if (ticks.Count > 0) view.OnTick(ticks[^1]);
        if (File.Exists(journal)) offset = new FileInfo(journal).Length;

        AnsiConsole.Live(view.Render()).AutoClear(false).Start(ctx =>
        {
            while (!cts.IsCancellationRequested)
            {
                var alive = GuardProcess() is not null;
                foreach (var line in ReadNewLines(journal, ref offset))
                {
                    if (Parse(line) is { } p)
                    {
                        if (p.Tick is { } t) view.OnTick(t);
                        else view.OnEvent(p.Event!);
                    }
                }
                if (!alive) view.OnEventOnce("(guard no esta en marcha; este panel solo lee el registro)");
                ctx.UpdateTarget(view.Render());
                ctx.Refresh();
                Thread.Sleep(1000);
            }
        });
        return 0;
    }

    public static Process? GuardProcess()
        => Process.GetProcessesByName("colab").FirstOrDefault(p => p.Id != Environment.ProcessId);

    private static (List<GuardTick> Ticks, List<string> Events) ReadJournal(string path, bool sinceLastStart)
    {
        var ticks = new List<GuardTick>();
        var events = new List<string>();
        if (!File.Exists(path)) return (ticks, events);
        foreach (var line in File.ReadLines(path))
        {
            if (Parse(line) is not { } p) continue;
            if (sinceLastStart && p.Kind == "start") { ticks.Clear(); events.Clear(); }
            if (p.Tick is { } t) ticks.Add(t); else events.Add(p.Event!);
        }
        return (ticks, events);
    }

    private static IEnumerable<string> ReadNewLines(string path, ref long offset)
    {
        if (!File.Exists(path)) return [];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length < offset) offset = 0;
        fs.Seek(offset, SeekOrigin.Begin);
        using var sr = new StreamReader(fs);
        var text = sr.ReadToEnd();
        offset = fs.Length;
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static (string Kind, GuardTick? Tick, string? Event)? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var e = doc.RootElement;
            var kind = e.GetProperty("kind").GetString() ?? "";
            if (kind == "tick")
            {
                var hw = e.GetProperty("Hardware").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetInt32() : (int?)null).ToArray();
                return (kind, new GuardTick(e.GetProperty("Ts").GetDateTime(), e.GetProperty("Elapsed").GetInt32(), e.GetProperty("Ok").GetBoolean(), hw,
                    e.GetProperty("Whea").GetInt32(), Num(e, "CpuLoad"), Num(e, "PackagePower"), e.GetProperty("State").GetString() ?? ""), null);
            }
            return (kind, null, $"{e.GetProperty("ts").GetDateTime():HH:mm:ss}  {kind}: {e.GetProperty("detail").GetString()}");
        }
        catch { return null; }
    }

    private static double? Num(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;
}
