namespace LegionCoLab.Core;

public sealed record Sample(
    DateTime Ts, int Elapsed, double? Clock, double? ClockEffective, double? Volt, double? Freq,
    double? Power, double? Temp, double? PackagePower, double? Tctl);

public sealed record SampleSummary(
    int Samples, double? ClockMedian, double? ClockEffectiveMedian, double? ClockEffectiveP10,
    double? VoltMedian, double? VoltMax, double? FreqMedian, double? PowerMedian,
    double? PackagePowerMedian, double? TempMedian, double? TempMax);

/// <summary>
/// Muestreo de un nucleo: reloj y efectivo (LHM) mas V/GHz/W/T de la tabla
/// PM del SMU. Acumula para las medianas del final. La primera lectura de
/// LHM tras abrirlo no tiene ventana APERF/MPERF: el que lo use debe
/// descartar una muestra al arrancar (<see cref="Prime"/>).
/// </summary>
public sealed class Sampler
{
    private readonly Telemetry? _tel;
    private readonly PmTable? _pm;
    private readonly int _core;
    private readonly DateTime _t0 = DateTime.Now;

    private readonly List<double> _clocks = [], _effs = [], _volts = [], _freqs = [], _powers = [], _pkgs = [], _temps = [];

    public Sampler(Telemetry? telemetry, PmTable? pm, int core)
    {
        _tel = telemetry is { IsAvailable: true } ? telemetry : null;
        _pm = pm is { IsAvailable: true } ? pm : null;
        _core = core;
    }

    public bool IsAvailable => _tel is not null || _pm is not null;

    public void Prime() => _tel?.Read(_core);

    public Sample Take()
    {
        var s = _tel?.Read(_core);
        var ok = _pm?.Refresh() ?? false;
        var c = ok ? _pm!.Core(_core) : default;

        Add(_clocks, s?.TargetClock); Add(_effs, s?.TargetClockEffective);
        Add(_volts, c.Volt); Add(_freqs, c.Freq); Add(_powers, c.Power);
        Add(_pkgs, s?.PackagePower); Add(_temps, c.Temp);

        return new Sample(DateTime.Now, (int)(DateTime.Now - _t0).TotalSeconds,
            s?.TargetClock, s?.TargetClockEffective, c.Volt, c.Freq, c.Power, c.Temp, s?.PackagePower, s?.Tctl);
    }

    public SampleSummary Summary() => new(
        Math.Max(_clocks.Count, _volts.Count),
        Median(_clocks), Median(_effs), Percentile(_effs, 0.10),
        Median(_volts), _volts.Count > 0 ? _volts.Max() : null,
        Median(_freqs), Median(_powers), Median(_pkgs),
        Median(_temps), _temps.Count > 0 ? _temps.Max() : null);

    private static void Add(List<double> xs, double? v) { if (v is { } d) xs.Add(d); }

    public static double? Median(List<double> xs) => Percentile(xs, 0.5);

    public static double? Percentile(List<double> xs, double p)
    {
        if (xs.Count == 0) return null;
        var s = xs.OrderBy(x => x).ToList();
        return s[(int)Math.Round(p * (s.Count - 1))];
    }
}
