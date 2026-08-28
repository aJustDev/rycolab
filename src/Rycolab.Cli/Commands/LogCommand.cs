using System.Text;
using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab dev log --out file.csv [--interval 2] [--minutes N]
/// One CSV row per sample while something else (Cinebench, a game) loads the
/// machine: package power, Tctl and CCD temperatures, effective clock (all
/// cores and per core), core voltages from the PM table, VID, and on Lenovo
/// Legion machines the fan speeds and EC temperatures. Written row by row.
/// `rycolab report --bench file.csv [--vs base.csv]` summarises it.
/// </summary>
public static class LogCommand
{
    public static int Run(Args args)
    {
        var outPath = args.Get("out");
        if (outPath is null) { Console.Error.WriteLine("Usage: rycolab dev log --out <file.csv> [--interval 2] [--minutes N]"); return 2; }
        var interval = args.GetInt("interval") ?? 2;
        var minutes = args.GetInt("minutes");
        outPath = Path.GetFullPath(outPath);

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

        var t0 = DateTime.Now;
        var end = minutes is > 0 ? t0.AddMinutes(minutes.Value) : DateTime.MaxValue;
        telemetry.Read();   // the first LHM read has no APERF/MPERF window
        Thread.Sleep(1000);
        Console.WriteLine($"  Logging to {outPath} every {interval} s{(minutes is > 0 ? $" for {minutes} min" : "")} (Ctrl+C to stop)");
        Console.WriteLine("      t    W pkg   Tctl   eff MHz   V avg   fan CPU/GPU/PCH");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        while (DateTime.Now < end && !cts.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var snap = telemetry.Read();
            var lhm = telemetry.IsAvailable ? telemetry.AllCores(cores) : [];
            var pmOk = pm.Refresh();
            var pmc = Enumerable.Range(0, cores).Select(c => pmOk ? pm.Core(c) : default).ToList();

            var effs = lhm.Select(s => s.ClockEffective).OfType<double>().ToList();
            var vids = lhm.Select(s => s.Vid).OfType<double>().ToList();
            var volts = pmc.Select(s => s.Volt).OfType<double>().ToList();
            var temps = pmc.Select(s => s.Temp).OfType<double>().ToList();

            var cells = new List<string>
            {
                now.ToString("yyyy-MM-dd HH:mm:ss"), ((int)(now - t0).TotalSeconds).ToString(),
                BenchLog.Cell(snap.PackagePower, 1), BenchLog.Cell(snap.Tctl, 1), BenchLog.Cell(snap.Ccd0Temp, 1), BenchLog.Cell(snap.Ccd1Temp, 1),
                BenchLog.Cell(effs.Count > 0 ? effs.Average() : null, 0),
                BenchLog.Cell(volts.Count > 0 ? volts.Average() : null, 4), BenchLog.Cell(volts.Count > 0 ? volts.Max() : null, 4),
                BenchLog.Cell(vids.Count > 0 ? vids.Average() : null, 4), BenchLog.Cell(temps.Count > 0 ? temps.Max() : null, 1),
                BenchLog.Cell(ec.CpuFanRpm), BenchLog.Cell(ec.GpuFanRpm), BenchLog.Cell(ec.PchFanRpm),
                BenchLog.Cell(ec.CpuTempC), BenchLog.Cell(ec.GpuTempC), BenchLog.Cell(ec.PchTempC),
            };
            cells.AddRange(Enumerable.Range(0, cores).Select(c => BenchLog.Cell(c < lhm.Count ? lhm[c].ClockEffective : null, 0)));
            cells.AddRange(pmc.Select(s => BenchLog.Cell(s.Volt, 4)));
            w.WriteLine(string.Join(",", cells));

            Console.WriteLine("  {0,5}s  {1,6}  {2,5}  {3,8}  {4,6}   {5}/{6}/{7}",
                cells[1], cells[2], cells[3], cells[6], cells[7], Or(cells[11]), Or(cells[12]), Or(cells[13]));
            cts.Token.WaitHandle.WaitOne(interval * 1000);
        }
        Console.WriteLine();
        Console.WriteLine($"  Done. Summary: rycolab report --bench \"{outPath}\"");
        return 0;
    }

    private static string Or(string s) => s.Length == 0 ? "-" : s;
}
