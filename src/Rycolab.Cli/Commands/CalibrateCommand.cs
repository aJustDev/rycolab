using Rycolab.Core;
using Rycolab.Core.Engines;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab dev calibrate [--core N] [--seconds 40]
/// Locates the per-core blocks of the SMU power table for this CPU's table
/// version. Takes the median table at idle and with one core loaded by
/// y-cruncher, and looks for 16-float windows where only the loaded core's
/// position moves. Each block is then identified by its loaded value
/// (power in W, voltage in V, temperature in C, frequency in GHz),
/// cross-checked with LibreHardwareMonitor. Writes pm-index.json.
/// </summary>
public static class CalibrateCommand
{
    public static int Run(Args args)
    {
        var core = args.GetInt("core") ?? 3;
        var seconds = args.GetInt("seconds") ?? 40;
        var config = Plan.LoadOrDefault();
        if (!Installer.HasYCruncher(config.YCruncherDir, config.AllEngines)) { Console.Error.WriteLine("y-cruncher missing; run `rycolab install`."); return 1; }

        using var co = new CoController();
        var pm = new PmTable(co.Cpu);
        if (!pm.HasTable) { Console.Error.WriteLine("The SMU power table is not readable on this CPU."); return 1; }
        Console.WriteLine($"  table version 0x{pm.Version:X}, {pm.Length} floats{(pm.Index is { } i ? $", current index power {i.Power} volt {i.Volt} temp {i.Temp} freq {i.Freq}" : ", no index yet")}");

        using var telemetry = new Telemetry();

        Console.WriteLine("  sampling the table at idle (15 s)...");
        var idle = MedianTable(pm, 15);

        var work = Path.Combine(AppPaths.Data, "calibrate");
        using var engine = new YCruncherEngine(config.YCruncherDir, config.Engines[0], ["SFTv4"], suspend: false);
        Console.WriteLine($"  loading core {core} with {config.Engines[0]} for {seconds} s...");
        engine.Start(core, work);
        float[] loaded;
        double? lhmPower = null, lhmClock = null, lhmTctl = null;
        var lhmPkgs = new List<double>();
        try
        {
            Thread.Sleep(5000);
            loaded = MedianTable(pm, seconds - 5, () =>
            {
                if (!telemetry.IsAvailable) return;
                var s = telemetry.Read(core);
                lhmPower = s.TargetPower ?? lhmPower;
                lhmClock = s.TargetClock ?? lhmClock;
                lhmTctl = s.Tctl ?? lhmTctl;
                // The median across the run survives LHM's garbage bursts.
                if (s.PackagePower is > 0 and < 250 and var pw) lhmPkgs.Add(pw);
            });
        }
        finally { engine.Stop(); }

        var n = Math.Min(idle.Length, loaded.Length);
        Console.WriteLine();
        if (telemetry.IsAvailable) Console.WriteLine($"  LHM under load: power {lhmPower:F2} W, clock {lhmClock:F0} MHz, Tctl {lhmTctl:F1} C");

        // Each block is a window of 16 floats. The loaded core's value, its idle value and
        // the other fifteen cores' values must all be plausible for the quantity; the
        // LibreHardwareMonitor reading breaks ties. Requiring "only the loaded core moves"
        // does not work: the neighbours warm up and change clocks too.
        int? power = Scan(idle, loaded, core, n, "power", lhmPower, 0.3,
            v => v is >= 2 and <= 40, i => i < 1.0, o => o is >= 0 and <= 40, minMoved: 1.0);
        // minMoved 1.0: an idle core to a fully loaded one always jumps more than
        // 1 GHz; a scalar that happens to sit near the clock does not (offset 33
        // moved 0.5 GHz on 2026-09-01 and beat the real block 349 on the tie-break).
        int? freq = Scan(idle, loaded, core, n, "freq", lhmClock / 1000.0, 0.06,
            v => v is >= 2.5 and <= 6.5, i => i is >= 0 and <= 6.5, o => o is >= 0.5 and <= 6.5, minMoved: 1.0, othersMinFraction: 0.5);
        int? temp = Scan(idle, loaded, core, n, "temp", lhmTctl, 0.15,
            v => v is >= 35 and <= 110, i => i is >= 20 and <= 100, o => o is >= 20 and <= 110, minMoved: 3);
        int? volt = Scan(idle, loaded, core, n, "volt", null, 0,
            v => v is >= 0.75 and <= 1.5, i => i is >= 0.3 and <= 1.5, o => o is >= 0.3 and <= 1.5, minMoved: 0.1);

        Console.WriteLine($"  power {power?.ToString() ?? "?"}   volt {volt?.ToString() ?? "?"}   temp {temp?.ToString() ?? "?"}   freq {freq?.ToString() ?? "?"}");
        if (power is null || volt is null || temp is null || freq is null)
        {
            Console.Error.WriteLine("  Could not identify all four blocks. Try another core or a longer run.");
            return 1;
        }
        var distinct = new[] { power.Value, volt.Value, temp.Value, freq.Value }.Distinct().Count();
        if (distinct < 4) { Console.Error.WriteLine("  Two blocks resolved to the same window; not saving."); return 1; }

        var lhmPkg = Sampler.Median(lhmPkgs);
        var pkg = ScanPackage(idle, loaded, n, [power.Value, volt.Value, temp.Value, freq.Value], lhmPkg);
        Console.WriteLine($"  package {pkg?.ToString() ?? "?"}{(lhmPkg is { } lp ? $"  (LHM median under load {lp:F1} W)" : "")}");

        var idx = new PmIndex(power.Value, volt.Value, temp.Value, freq.Value, pkg);
        PmIndex.Save(pm.Version, idx);
        Console.WriteLine($"  Saved to {PmIndex.File} for version 0x{pm.Version:X}.");
        return 0;
    }

    /// <summary>
    /// The package-power scalar: idle a handful of watts, up with one core
    /// loaded, never below the sum of the per-core power block. Of the
    /// plausible candidates the LARGEST loaded value wins: the package is the
    /// superset, so the core-domain scalar (offset 20 on 0x621202, near zero
    /// at a parked idle) always reads below the true package (offset 3,
    /// which carries the ~15 W IO-die floor).
    /// </summary>
    private static int? ScanPackage(float[] idle, float[] loaded, int n, int[] blocks, double? lhmPkg)
    {
        double sumIdle = 0, sumLoaded = 0;
        for (var k = 0; k < Topology.MaxCores; k++) { sumIdle += idle[blocks[0] + k]; sumLoaded += loaded[blocks[0] + k]; }

        var found = new List<(int Start, double Idle, double Loaded)>();
        for (var j = 0; j < n; j++)
        {
            if (blocks.Any(b => j >= b && j < b + Topology.MaxCores)) continue;
            double i = idle[j], v = loaded[j];
            if (i is < 3 or > 30 || v is < 15 or > 90 || v - i < 5) continue;
            if (v < sumLoaded || i < sumIdle) continue;
            // The SoC floor (package minus cores) barely moves under a one-core
            // load; a budget/percentage field (offset 413 on 0x621202) does not
            // keep that invariant and is rejected here.
            if (Math.Abs((v - sumLoaded) - (i - sumIdle)) > 6) continue;
            found.Add((j, i, v));
        }
        Console.WriteLine($"  package candidates: {(found.Count == 0 ? "none" : string.Join(", ", found.Select(f => $"{f.Start} ({f.Loaded:F3} vs {f.Idle:F3})")))}{(lhmPkg is { } r ? $"  (LHM median {r:F1} W)" : "")}");
        return found.Count == 0 ? null : found.OrderByDescending(f => f.Loaded).First().Start;
    }

    private static float[] MedianTable(PmTable pm, int seconds, Action? each = null)
    {
        var tables = new List<float[]>();
        var t0 = DateTime.Now;
        while ((DateTime.Now - t0).TotalSeconds < seconds)
        {
            if (pm.Refresh()) tables.Add(pm.Raw);
            each?.Invoke();
            Thread.Sleep(1000);
        }
        if (tables.Count < 3) throw new InvalidOperationException("too few table reads.");
        var n = tables[0].Length;
        var median = new float[n];
        for (var j = 0; j < n; j++)
        {
            var col = tables.Select(t => t[j]).OrderBy(x => x).ToList();
            median[j] = col[col.Count / 2];
        }
        return median;
    }

    private static int? Scan(float[] idle, float[] loaded, int core, int n, string what, double? reference, double tolerance,
        Func<double, bool> loadedOk, Func<double, bool> idleOk, Func<double, bool> otherOk, double minMoved, double othersMinFraction = 1.0)
    {
        var found = new List<(int Start, double Value, double Moved)>();
        for (var start = 0; start + Topology.MaxCores <= n; start++)
        {
            double v = loaded[start + core], i = idle[start + core];
            if (!loadedOk(v) || !idleOk(i) || Math.Abs(v - i) < minMoved) continue;
            var others = Enumerable.Range(0, Topology.MaxCores).Where(k => k != core).Select(k => (double)loaded[start + k]).ToList();
            if (others.Count(otherOk) < othersMinFraction * others.Count) continue;
            if (reference is { } r && tolerance > 0 && Math.Abs(v - r) > tolerance * Math.Max(r, 1)) continue;
            found.Add((start, v, Math.Abs(v - i)));
        }
        Console.WriteLine($"  {what,-6} candidates: {(found.Count == 0 ? "none" : string.Join(", ", found.Select(f => $"{f.Start} ({f.Value:F3}, moved {f.Moved:F3})")))}");
        if (found.Count == 0) return null;
        return reference is { } rr
            ? found.OrderBy(f => Math.Abs(f.Value - rr)).First().Start
            : found.OrderByDescending(f => f.Moved).First().Start;
    }
}
