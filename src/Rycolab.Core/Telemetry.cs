using LibreHardwareMonitor.Hardware;

namespace Rycolab.Core;

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
    double? Ccd0Temp,
    double? Ccd1Temp,
    double? MaxCoreClock,
    int? TargetCore,
    double? TargetClock,
    double? TargetClockEffective,
    double? TargetVid,
    double? TargetPower);

/// <summary>
/// Sensors via LibreHardwareMonitorLib.
///
/// Optional on purpose: if it does not start, the tool carries on, because
/// configuration verification is done against the SMU and does not depend on it.
///
/// Per-core names are matched EXACTLY. "Core #1" is a substring of "Core #10",
/// so a Contains lookup returns the wrong core.
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
            if (!IsAvailable) Unavailable = "LibreHardwareMonitor exposed no CPU sensors.";
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

    /// <summary>Exact name, no substrings.</summary>
    private double? Exact(SensorType type, string name)
        => _all.FirstOrDefault(s => s.SensorType == type &&
                                    string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>First sensor whose name contains any of the patterns. Only for unique sensors.</summary>
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

    // LibreHardwareMonitor numbers cores from 1; we number from 0.
    private static string CoreLabel(int coreIndex) => $"Core #{coreIndex + 1}";

    public double? CoreClock(int coreIndex) => Exact(SensorType.Clock, CoreLabel(coreIndex));
    public double? CoreClockEffective(int coreIndex) => Exact(SensorType.Clock, $"{CoreLabel(coreIndex)} (Effective)");
    public double? CoreVid(int coreIndex) => Exact(SensorType.Voltage, $"{CoreLabel(coreIndex)} VID");
    public double? CorePower(int coreIndex) => Exact(SensorType.Power, $"{CoreLabel(coreIndex)} (SMU)");

    /// <summary>Total CPU load in %, refreshed.</summary>
    public double? CpuLoad()
    {
        if (!IsAvailable) return null;
        Refresh();
        return Exact(SensorType.Load, "CPU Total") ?? Fuzzy(SensorType.Load, "Total");
    }

    public TelemetrySnapshot Read(int? targetCore = null)
    {
        if (!IsAvailable)
            return new TelemetrySnapshot(DateTime.Now, null, null, null, null, null, targetCore, null, null, null, null);

        Refresh();

        return new TelemetrySnapshot(
            Timestamp: DateTime.Now,
            PackagePower: Exact(SensorType.Power, "Package") ?? Fuzzy(SensorType.Power, "Package"),
            Tctl: Exact(SensorType.Temperature, "Core (Tctl/Tdie)") ?? Fuzzy(SensorType.Temperature, "Tctl"),
            Ccd0Temp: Exact(SensorType.Temperature, Topology.CcdTempSensor(0)),
            Ccd1Temp: Exact(SensorType.Temperature, Topology.CcdTempSensor(1)),
            MaxCoreClock: Fuzzy(SensorType.Clock, "Cores (Average)"),
            TargetCore: targetCore,
            TargetClock: targetCore.HasValue ? CoreClock(targetCore.Value) : null,
            TargetClockEffective: targetCore.HasValue ? CoreClockEffective(targetCore.Value) : null,
            TargetVid: targetCore.HasValue ? CoreVid(targetCore.Value) : null,
            TargetPower: targetCore.HasValue ? CorePower(targetCore.Value) : null);
    }

    /// <summary>
    /// Per-core snapshot.
    ///
    /// About Vid: on the 9955HX3D, LibreHardwareMonitor's "Core #N VID" is NOT a
    /// per-core voltage: all 16 return the same value and move together
    /// (measured 2026-08-26: 0.269 V on all sixteen with one core at 100 %,
    /// physically impossible). Kept for diagnostics, not used as a signal. What
    /// does discriminate per core is Power and ClockEffective.
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
        try { _computer?.Close(); } catch { /* nothing to do */ }
    }
}
