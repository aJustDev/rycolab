using Rycolab.Core;
using Rycolab.Core.Engines;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab dev calibrate [--core N] [--seconds 40]
/// Locates the per-core blocks of the SMU power table for this CPU's table
/// version: loads one core with y-cruncher, takes the median table, and
/// looks for 16-float windows where that core stands out from the other
/// fifteen. Each block is identified by its value range and cross-checked
/// with LibreHardwareMonitor. Writes pm-index.json.
/// </summary>
public static class CalibrateCommand
{
    public static int Run(Args args)
    {
        var core = args.GetInt("core") ?? 3;
        var seconds = args.GetInt("seconds") ?? 40;
        var config = Plan.LoadOrDefault();
        if (!Installer.HasYCruncher(config.YCruncherDir)) { Console.Error.WriteLine("y-cruncher missing; run `rycolab install`."); return 1; }

        using var co = new CoController();
        var pm = new PmTable(co.Cpu);
        if (!pm.HasTable) { Console.Error.WriteLine("The SMU power table is not readable on this CPU."); return 1; }
        Console.WriteLine($"  table version 0x{pm.Version:X}, {pm.Length} floats{(pm.Index is { } i ? $", current index power {i.Power} volt {i.Volt} temp {i.Temp} freq {i.Freq}" : ", no index yet")}");

        using var telemetry = new Telemetry();
        var work = Path.Combine(AppPaths.Data, "calibrate");
        using var engine = new YCruncherEngine(config.YCruncherDir, config.Engines[0], ["SFTv4"], suspend: false);
        Console.WriteLine($"  loading core {core} with {config.Engines[0]} for {seconds} s...");
        engine.Start(core, work);

        var tables = new List<float[]>();
        double? lhmPower = null, lhmClock = null, lhmTctl = null;
        var t0 = DateTime.Now;
        try
        {
            Thread.Sleep(5000);
            while ((DateTime.Now - t0).TotalSeconds < seconds)
            {
                if (pm.Refresh()) tables.Add(pm.Raw);
                if (telemetry.IsAvailable)
                {
                    var s = telemetry.Read(core);
                    lhmPower = s.TargetPower ?? lhmPower;
                    lhmClock = s.TargetClock ?? lhmClock;
                    lhmTctl = s.Tctl ?? lhmTctl;
                }
                Thread.Sleep(1000);
            }
        }
        finally { engine.Stop(); }

        if (tables.Count < 5) { Console.Error.WriteLine("  too few table reads."); return 1; }
        var n = tables[0].Length;
        var median = new float[n];
        for (var j = 0; j < n; j++)
        {
            var col = tables.Select(t => t[j]).OrderBy(x => x).ToList();
            median[j] = col[col.Count / 2];
        }

        // Windows of 16 where entry [core] stands out from the other 15.
        var candidates = new List<(int Start, double Value, double Others)>();
        for (var start = 0; start + Topology.MaxCores <= n; start++)
        {
            var v = median[start + core];
            var others = Enumerable.Range(0, Topology.MaxCores).Where(k => k != core).Select(k => (double)median[start + k]).ToList();
            var med = others.OrderBy(x => x).ElementAt(others.Count / 2);
            var spread = others.Max() - others.Min();
            if (v > med * 1.5 + 0.05 && v - med > 3 * Math.Max(spread, 0.01) && v > 0.3)
                candidates.Add((start, v, med));
        }

        int? power = Pick(candidates, 2, 40, lhmPower, 0.3);
        int? freq = Pick(candidates, 2.5, 6.5, lhmClock / 1000.0, 0.1);
        int? temp = Pick(candidates, 35, 110, lhmTctl, 0.2);
        int? volt = Pick(candidates.Where(c => c.Start != power && c.Start != freq && c.Start != temp).ToList(), 0.7, 1.6, null, 0);

        Console.WriteLine();
        Console.WriteLine($"  candidates: {string.Join(", ", candidates.Select(c => $"{c.Start} ({c.Value:F3} vs {c.Others:F3})"))}");
        Console.WriteLine($"  power {power?.ToString() ?? "?"}   volt {volt?.ToString() ?? "?"}   temp {temp?.ToString() ?? "?"}   freq {freq?.ToString() ?? "?"}");
        if (power is null || volt is null || temp is null || freq is null)
        {
            Console.Error.WriteLine("  Could not identify all four blocks. Try another core or a longer run.");
            return 1;
        }
        var idx = new PmIndex(power.Value, volt.Value, temp.Value, freq.Value);
        PmIndex.Save(pm.Version, idx);
        Console.WriteLine($"  Saved to {PmIndex.File} for version 0x{pm.Version:X}.");
        return 0;
    }

    private static int? Pick(List<(int Start, double Value, double Others)> cands, double min, double max, double? reference, double tolerance)
    {
        var inRange = cands.Where(c => c.Value >= min && c.Value <= max).ToList();
        if (inRange.Count == 0) return null;
        if (reference is { } r && tolerance > 0)
        {
            var close = inRange.Where(c => Math.Abs(c.Value - r) <= tolerance * Math.Max(r, 1)).OrderBy(c => Math.Abs(c.Value - r)).ToList();
            if (close.Count > 0) return close[0].Start;
        }
        return inRange.OrderByDescending(c => c.Value - c.Others).First().Start;
    }
}
