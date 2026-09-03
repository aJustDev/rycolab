using Rycolab.Core.Legion;
using System.Text;
using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab dev log --out file.csv [--name bench] [--interval 2] [--minutes N]
/// One row per sample while something else (Cinebench, a game) loads the
/// machine: package power, Tctl and CCD temperatures, effective clock (all
/// cores and per core), core voltages from the PM table, VID, and on Lenovo
/// Legion machines the fan speeds and EC temperatures. Every row goes to the
/// database (`bench`, `bench_samples`) and to the CSV, row by row.
/// `rycolab report --bench file.csv [--vs base.csv]` summarises the CSV.
/// </summary>
public static class LogCommand
{
    public static int Run(Args args)
    {
        var outPath = args.Get("out");
        if (outPath is null) { Console.Error.WriteLine("Usage: rycolab dev log --out <file.csv> [--name bench] [--interval 2] [--minutes N]"); return 2; }
        var interval = args.GetInt("interval") ?? 2;
        var minutes = args.GetInt("minutes");
        outPath = Path.GetFullPath(outPath);
        var benchName = args.Get("name") ?? Path.GetFileNameWithoutExtension(outPath);

        using var telemetry = new Telemetry();
        using var co = new CoController();
        var pm = new PmTable(co.Cpu);
        using var ec = new LenovoEc();
        var cores = co.CoreCount;

        Console.WriteLine();
        Console.WriteLine($"  LHM {(telemetry.IsAvailable ? "ok" : "unavailable: " + telemetry.Unavailable)}   PM table {(pm.IsAvailable ? $"ok (v0x{pm.Version:X})" : pm.HasTable ? "not calibrated (per-core columns empty)" : "unavailable")}   Lenovo EC {(ec.IsAvailable ? "ok" : "not present (fan columns empty)")}");
        if (!telemetry.IsAvailable && !pm.IsAvailable) return 1;

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        var append = File.Exists(outPath) && new FileInfo(outPath).Length > 0;
        using var w = new StreamWriter(outPath, append, new UTF8Encoding(false)) { AutoFlush = true };
        if (!append) w.WriteLine(string.Join(",", BenchLog.Columns(cores)));
        using var store = Store.Open();
        var benchId = store.BeginBench(benchName, interval);

        var t0 = DateTime.Now;
        var end = minutes is > 0 ? t0.AddMinutes(minutes.Value) : DateTime.MaxValue;
        telemetry.Read();   // the first LHM read has no APERF/MPERF window
        Thread.Sleep(1000);
        Console.WriteLine($"  Logging to {outPath} every {interval} s{(minutes is > 0 ? $" for {minutes} min" : "")} (Ctrl+C to stop)");
        Console.WriteLine("      t    W pkg   Tctl   eff MHz   V avg   fan CPU/GPU/PCH   line");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        while (DateTime.Now < end && !cts.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var snap = telemetry.Read();
            var lhm = telemetry.IsAvailable ? telemetry.AllCores(cores) : [];
            var pmOk = pm.Refresh();
            var bat = BatteryInfo.Read();
            var pmc = Enumerable.Range(0, cores).Select(c => pmOk ? pm.Core(c) : default).ToList();

            var effs = lhm.Select(s => s.ClockEffective).OfType<double>().ToList();
            var vids = lhm.Select(s => s.Vid).OfType<double>().ToList();
            var volts = pmc.Select(s => s.Volt).OfType<double>().ToList();
            var temps = pmc.Select(s => s.Temp).OfType<double>().ToList();
            var elapsed = (int)(now - t0).TotalSeconds;
            double? effAvg = effs.Count > 0 ? effs.Average() : null, vAvg = volts.Count > 0 ? volts.Average() : null, vMax = volts.Count > 0 ? volts.Max() : null,
                vidAvg = vids.Count > 0 ? vids.Average() : null, tempMax = temps.Count > 0 ? temps.Max() : null;
            int? fanCpu = ec.CpuFanRpm, fanGpu = ec.GpuFanRpm, fanPch = ec.PchFanRpm, ecCpu = ec.CpuTempC, ecGpu = ec.GpuTempC, ecPch = ec.PchTempC;
            var perCoreEff = Enumerable.Range(0, cores).Select(c => c < lhm.Count ? lhm[c].ClockEffective : null).ToList();
            var perCoreVolt = pmc.Select(s => s.Volt).ToList();

            var cells = new List<string>
            {
                now.ToString("yyyy-MM-dd HH:mm:ss"), elapsed.ToString(),
                BenchLog.Cell(snap.PackagePower, 1), BenchLog.Cell(snap.Tctl, 1), BenchLog.Cell(snap.Ccd0Temp, 1), BenchLog.Cell(snap.Ccd1Temp, 1),
                BenchLog.Cell(effAvg, 0), BenchLog.Cell(vAvg, 4), BenchLog.Cell(vMax, 4), BenchLog.Cell(vidAvg, 4), BenchLog.Cell(tempMax, 1),
                BenchLog.Cell(fanCpu), BenchLog.Cell(fanGpu), BenchLog.Cell(fanPch), BenchLog.Cell(ecCpu), BenchLog.Cell(ecGpu), BenchLog.Cell(ecPch),
                BenchLog.Cell(bat.OnAc is { } ac ? (ac ? 1 : 0) : null), BenchLog.Cell(bat.DischargeW, 2), BenchLog.Cell(bat.Percent, 1), BenchLog.Cell(bat.RemainingWh, 2),
            };
            cells.AddRange(perCoreEff.Select(e => BenchLog.Cell(e, 0)));
            cells.AddRange(perCoreVolt.Select(v => BenchLog.Cell(v, 4)));
            w.WriteLine(string.Join(",", cells));
            store.AddBenchSample(benchId, now, elapsed, snap.PackagePower, snap.Tctl, snap.Ccd0Temp, snap.Ccd1Temp, effAvg, vAvg, vMax, vidAvg, tempMax,
                fanCpu, fanGpu, fanPch, ecCpu, ecGpu, ecPch, bat.OnAc, bat.DischargeW, bat.Percent, bat.RemainingWh,
                System.Text.Json.JsonSerializer.Serialize(new { eff = perCoreEff, v = perCoreVolt }));

            Console.WriteLine("  {0,5}s  {1,6}  {2,5}  {3,8}  {4,6}   {5}/{6}/{7}   {8}",
                cells[1], cells[2], cells[3], cells[6], cells[7], Or(cells[11]), Or(cells[12]), Or(cells[13]), cells[17] == "0" ? cells[18] + " W bat" : "AC");
            cts.Token.WaitHandle.WaitOne(interval * 1000);
        }
        store.EndBench(benchId);
        Console.WriteLine();
        Console.WriteLine($"  Done. Summary: rycolab report --bench \"{outPath}\"   (rows also in the database: bench {benchName})");
        return 0;
    }

    private static string Or(string s) => s.Length == 0 ? "-" : s;
}
