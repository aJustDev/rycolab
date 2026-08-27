using System.Text.Json;
using LegionCoLab.Core;

namespace LegionCoLab.Cli.Commands;

/// <summary>
/// Muestrea a intervalo fijo un nucleo mientras otra cosa lo carga: reloj y
/// reloj efectivo (LibreHardwareMonitor), y tension, frecuencia, potencia y
/// temperatura del nucleo desde la tabla de potencia del SMU (PmTable).
/// Una linea JSON por muestra; al final, medianas.
///
/// --raw anade a cada muestra la tabla PM completa (613 floats en el
/// 9955HX3D). Sirve para localizar posiciones por diferencia entre margenes
/// (scripts/pm-diff.ps1).
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
            Console.Error.WriteLine($"Telemetria LHM no disponible: {telemetry.Unavailable}");

        using var co = new CoController();
        var pm = new PmTable(co.Cpu);
        if (!pm.IsAvailable)
            Console.Error.WriteLine("Tabla de potencia del SMU no disponible.");
        if (!telemetry.IsAvailable && !pm.IsAvailable) return 1;
        if (raw && !pm.IsAvailable) raw = false;

        StreamWriter? w = null;
        if (jsonl is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonl))!);
            w = new StreamWriter(jsonl, append: false) { AutoFlush = true };
        }

        var clocks = new List<double>();
        var effs = new List<double>();
        var volts = new List<double>();
        var freqs = new List<double>();
        var powers = new List<double>();
        var pkgs = new List<double>();
        var temps = new List<double>();

        // La primera lectura tras abrir LHM no tiene ventana de tiempo para el
        // reloj efectivo (APERF/MPERF): se descarta.
        telemetry.Read(core);
        Thread.Sleep(interval);

        var t0 = DateTime.Now;
        var n = 0;
        Console.WriteLine($"  nucleo {core}  {seconds}s  cada {interval} ms   tabla PM v0x{pm.Version:X} ({pm.Length} floats){(raw ? "  +volcado" : "")}");
        Console.WriteLine("      t   reloj   efect.    V nuc.   GHz nuc.   W nuc.   W paq.   T nuc.");
        while ((DateTime.Now - t0).TotalSeconds < seconds)
        {
            var s = telemetry.Read(core);
            var ok = pm.Refresh();
            var c = ok ? pm.Core(core) : default;
            n++;
            if (s.TargetClock is { } cl) clocks.Add(cl);
            if (s.TargetClockEffective is { } e) effs.Add(e);
            if (c.Volt is { } v) volts.Add(v);
            if (c.Freq is { } f) freqs.Add(f);
            if (c.Power is { } p) powers.Add(p);
            if (s.PackagePower is { } pk) pkgs.Add(pk);
            if (c.Temp is { } t) temps.Add(t);

            var el = (int)(DateTime.Now - t0).TotalSeconds;
            Console.WriteLine("  {0,4}s  {1,6}  {2,6}  {3,8}  {4,9}  {5,7}  {6,7}  {7,7}",
                el, F(s.TargetClock, 0), F(s.TargetClockEffective, 0), F(c.Volt, 4), F(c.Freq, 3), F(c.Power, 2), F(s.PackagePower, 1), F(c.Temp, 1));

            w?.WriteLine(JsonSerializer.Serialize(new
            {
                ts = s.Timestamp,
                elapsed = el,
                core,
                clock = s.TargetClock,
                clockEffective = s.TargetClockEffective,
                volt = c.Volt,
                freq = c.Freq,
                power = c.Power,
                temp = c.Temp,
                packagePower = s.PackagePower,
                tctl = s.Tctl,
                ccd0 = s.Ccd0Temp,
                ccd1 = s.Ccd1Temp,
                pmTable = raw && ok ? pm.Raw : null,
            }));

            Thread.Sleep(interval);
        }

        var summary = new
        {
            core,
            samples = n,
            seconds,
            pmTableVersion = pm.Version,
            clockMedian = Median(clocks),
            clockEffectiveMedian = Median(effs),
            clockEffectiveP10 = Percentile(effs, 0.10),
            voltMedian = Median(volts),
            voltMax = volts.Count > 0 ? volts.Max() : (double?)null,
            freqMedian = Median(freqs),
            powerMedian = Median(powers),
            packagePowerMedian = Median(pkgs),
            tempMedian = Median(temps),
            tempMax = temps.Count > 0 ? temps.Max() : (double?)null,
        };

        Console.WriteLine();
        Console.WriteLine($"  RESUMEN nucleo {core}: {n} muestras   reloj {F(summary.clockMedian, 0)}   efectivo {F(summary.clockEffectiveMedian, 0)} (p10 {F(summary.clockEffectiveP10, 0)})   " +
                          $"V {F(summary.voltMedian, 4)} (max {F(summary.voltMax, 4)})   GHz {F(summary.freqMedian, 3)}   W nucleo {F(summary.powerMedian, 2)}   W paquete {F(summary.packagePowerMedian, 1)}   T {F(summary.tempMedian, 1)} (max {F(summary.tempMax, 1)})");

        if (summaryPath is not null) Journal.WriteJsonFile(Path.GetFullPath(summaryPath), summary);

        w?.Dispose();
        return 0;
    }

    private static string F(double? v, int dec) => v?.ToString("F" + dec) ?? "-";

    private static double? Median(List<double> xs) => Percentile(xs, 0.5);

    private static double? Percentile(List<double> xs, double p)
    {
        if (xs.Count == 0) return null;
        var s = xs.OrderBy(x => x).ToList();
        return s[(int)Math.Round(p * (s.Count - 1))];
    }
}
