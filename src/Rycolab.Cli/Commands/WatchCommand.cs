using System.Text.Json;
using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// Samples one core at a fixed interval while something else loads it:
/// clock and effective clock (LibreHardwareMonitor), and voltage, frequency,
/// power and temperature of the core from the SMU power table (PmTable).
/// One JSON line per sample; medians at the end.
///
/// --raw adds the full PM table to every sample (613 floats on the
/// 9955HX3D). Used to locate positions by diffing two margins.
/// </summary>
public static class WatchCommand
{
    public static int Run(Args args)
    {
        var core = int.Parse(args.Get("core", "0"));
        var seconds = int.Parse(args.Get("seconds", "180"));
        var interval = int.Parse(args.Get("interval", "1000"));
        var jsonl = args.Get("jsonl");
        var summaryPath = args.Get("summary");
        var raw = args.Has("raw");

        using var telemetry = new Telemetry();
        if (!telemetry.IsAvailable)
            Console.Error.WriteLine($"LHM telemetry unavailable: {telemetry.Unavailable}");

        using var co = new CoController();
        var pm = new PmTable(co.Cpu);
        if (!pm.IsAvailable)
            Console.Error.WriteLine("SMU power table unavailable.");
        if (!telemetry.IsAvailable && !pm.IsAvailable) return 1;
        if (raw && !pm.IsAvailable) raw = false;

        StreamWriter? w = null;
        if (jsonl is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonl))!);
            w = new StreamWriter(jsonl, append: false) { AutoFlush = true };
        }

        var sampler = new Sampler(telemetry, pm, core);
        sampler.Prime();   // the first LHM read has no APERF/MPERF window
        Thread.Sleep(interval);

        var t0 = DateTime.Now;
        var n = 0;
        Console.WriteLine($"  core {core}  {seconds}s  every {interval} ms   PM table v0x{pm.Version:X} ({pm.Length} floats){(raw ? "  +dump" : "")}");
        Console.WriteLine("      t   clock   effect.   V core    GHz core   W core   W pkg    T core");
        while ((DateTime.Now - t0).TotalSeconds < seconds)
        {
            var s = sampler.Take();
            n++;
            var el = (int)(DateTime.Now - t0).TotalSeconds;
            Console.WriteLine("  {0,4}s  {1,6}  {2,6}  {3,8}  {4,9}  {5,7}  {6,7}  {7,7}",
                el, F(s.Clock, 0), F(s.ClockEffective, 0), F(s.Volt, 4), F(s.Freq, 3), F(s.Power, 2), F(s.PackagePower, 1), F(s.Temp, 1));

            w?.WriteLine(JsonSerializer.Serialize(new
            {
                ts = s.Ts,
                elapsed = el,
                core,
                clock = s.Clock,
                clockEffective = s.ClockEffective,
                volt = s.Volt,
                freq = s.Freq,
                power = s.Power,
                temp = s.Temp,
                packagePower = s.PackagePower,
                tctl = s.Tctl,
                pmTable = raw && pm.IsAvailable ? pm.Raw : null,
            }));

            Thread.Sleep(interval);
        }

        var m = sampler.Summary();
        var summary = new
        {
            core,
            samples = n,
            seconds,
            pmTableVersion = pm.Version,
            clockMedian = m.ClockMedian,
            clockEffectiveMedian = m.ClockEffectiveMedian,
            clockEffectiveP10 = m.ClockEffectiveP10,
            voltMedian = m.VoltMedian,
            voltMax = m.VoltMax,
            freqMedian = m.FreqMedian,
            powerMedian = m.PowerMedian,
            packagePowerMedian = m.PackagePowerMedian,
            tempMedian = m.TempMedian,
            tempMax = m.TempMax,
        };

        Console.WriteLine();
        Console.WriteLine($"  SUMMARY core {core}: {n} samples   clock {F(m.ClockMedian, 0)}   effective {F(m.ClockEffectiveMedian, 0)} (p10 {F(m.ClockEffectiveP10, 0)})   " +
                          $"V {F(m.VoltMedian, 4)} (max {F(m.VoltMax, 4)})   GHz {F(m.FreqMedian, 3)}   W core {F(m.PowerMedian, 2)}   W package {F(m.PackagePowerMedian, 1)}   T {F(m.TempMedian, 1)} (max {F(m.TempMax, 1)})");

        if (summaryPath is not null) Journal.WriteJsonFile(Path.GetFullPath(summaryPath), summary);

        w?.Dispose();
        return 0;
    }

    private static string F(double? v, int dec) => v?.ToString("F" + dec) ?? "-";
}
