using LibreHardwareMonitor.Hardware;

namespace LegionCoLab.Core;

public readonly record struct CoreSample(
    int Core,
    double? Clock,
    double? ClockEffective,
    double? Vid,
    double? Power);

public readonly record struct TelemetrySnapshot(
    DateTime Timestamp,
    double? PackagePower,
    double? Tctl,
    double? Ccd1Temp,
    double? Ccd2Temp,
    double? MaxCoreClock,
    int? TargetCore,
    double? TargetClock,
    double? TargetClockEffective,
    double? TargetVid,
    double? TargetPower);

/// <summary>
/// Sensores via LibreHardwareMonitorLib.
///
/// Opcional a proposito: si no arranca, el arnes sigue, porque la verificacion
/// de configuracion se hace contra el SMU y no depende de aqui.
///
/// Los nombres por nucleo se casan EXACTOS. "Core #1" es subcadena de
/// "Core #10", asi que una busqueda por Contains devuelve el nucleo equivocado.
/// </summary>
public sealed class Telemetry : IDisposable
{
    private readonly Computer? _computer;
    private readonly List<ISensor> _all = [];

    public bool IsAvailable { get; }
    public string? Unavailable { get; }

    public Telemetry()
    {
        try
        {
            _computer = new Computer { IsCpuEnabled = true };
            _computer.Open();
            Refresh();
            IsAvailable = _all.Count > 0;
            if (!IsAvailable) Unavailable = "LibreHardwareMonitor no expuso ningun sensor de CPU.";
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Unavailable = ex.Message;
        }
    }

    private void Refresh()
    {
        if (_computer is null) return;
        _all.Clear();
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType != HardwareType.Cpu) continue;
            hw.Update();
            _all.AddRange(hw.Sensors);
            foreach (var sub in hw.SubHardware)
            {
                sub.Update();
                _all.AddRange(sub.Sensors);
            }
        }
    }

    /// <summary>Nombre exacto, sin subcadenas.</summary>
    private double? Exact(SensorType type, string name)
        => _all.FirstOrDefault(s => s.SensorType == type &&
                                    string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>Primer sensor cuyo nombre contenga alguno de los patrones. Solo para sensores unicos.</summary>
    private double? Fuzzy(SensorType type, params string[] patterns)
    {
        foreach (var p in patterns)
        {
            var s = _all.FirstOrDefault(x => x.SensorType == type &&
                                             x.Name.Contains(p, StringComparison.OrdinalIgnoreCase));
            if (s?.Value is { } v) return v;
        }
        return null;
    }

    // LibreHardwareMonitor numera los nucleos desde 1; nosotros desde 0.
    private static string CoreLabel(int coreIndex) => $"Core #{coreIndex + 1}";

    public double? CoreClock(int coreIndex) => Exact(SensorType.Clock, CoreLabel(coreIndex));
    public double? CoreClockEffective(int coreIndex) => Exact(SensorType.Clock, $"{CoreLabel(coreIndex)} (Effective)");
    public double? CoreVid(int coreIndex) => Exact(SensorType.Voltage, $"{CoreLabel(coreIndex)} VID");
    public double? CorePower(int coreIndex) => Exact(SensorType.Power, $"{CoreLabel(coreIndex)} (SMU)");

    public TelemetrySnapshot Read(int? targetCore = null)
    {
        if (!IsAvailable)
            return new TelemetrySnapshot(DateTime.Now, null, null, null, null, null, targetCore, null, null, null, null);

        Refresh();

        return new TelemetrySnapshot(
            Timestamp: DateTime.Now,
            PackagePower: Exact(SensorType.Power, "Package") ?? Fuzzy(SensorType.Power, "Package"),
            Tctl: Exact(SensorType.Temperature, "Core (Tctl/Tdie)") ?? Fuzzy(SensorType.Temperature, "Tctl"),
            Ccd1Temp: Exact(SensorType.Temperature, "CCD1 (Tdie)"),
            Ccd2Temp: Exact(SensorType.Temperature, "CCD2 (Tdie)"),
            MaxCoreClock: Fuzzy(SensorType.Clock, "Cores (Average)"),
            TargetCore: targetCore,
            TargetClock: targetCore.HasValue ? CoreClock(targetCore.Value) : null,
            TargetClockEffective: targetCore.HasValue ? CoreClockEffective(targetCore.Value) : null,
            TargetVid: targetCore.HasValue ? CoreVid(targetCore.Value) : null,
            TargetPower: targetCore.HasValue ? CorePower(targetCore.Value) : null);
    }

    /// <summary>
    /// Instantanea por nucleo.
    ///
    /// OJO con Vid: en el 9955HX3D, "Core #N VID" de LibreHardwareMonitor NO es
    /// un voltaje por nucleo — los 16 devuelven el mismo valor y se mueven en
    /// bloque (medido el 26/08/2026: 0,269 V en los dieciseis con un solo nucleo
    /// al 100 %, fisicamente imposible). Se conserva por diagnostico, pero no se
    /// usa como señal. Los que si discriminan son Power y ClockEffective.
    /// </summary>
    public IReadOnlyList<CoreSample> AllCores(int coreCount)
    {
        Refresh();
        return Enumerable.Range(0, coreCount)
            .Select(i => new CoreSample(i, CoreClock(i), CoreClockEffective(i), CoreVid(i), CorePower(i)))
            .ToList();
    }

    public IEnumerable<(string Type, string Name, float? Value)> DumpAll()
    {
        Refresh();
        return _all
            .OrderBy(s => s.SensorType.ToString())
            .ThenBy(s => s.Name)
            .Select(s => (s.SensorType.ToString(), s.Name, s.Value));
    }

    public void Dispose()
    {
        try { _computer?.Close(); } catch { /* nada que hacer */ }
    }
}
