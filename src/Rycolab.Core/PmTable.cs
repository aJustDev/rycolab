using ZenStates.Core;

namespace Rycolab.Core;

public readonly record struct PmCoreSample(double? Volt, double? Freq, double? Power, double? Temp);

/// <summary>
/// Per-core reads from the SMU power-management table (the raw floats
/// ZenStates exposes). LibreHardwareMonitor gives no per-core voltage on
/// these chips; the table does.
///
/// The indices were found EMPIRICALLY on 2026-08-27 (Ryzen 9 9955HX3D) by
/// diffing two margins with a single loaded core: four blocks of 16
/// consecutive floats where only position base+11 moved with core 11 under
/// load, and whose values match LibreHardwareMonitor (power, temperature,
/// clock). Verified for table version 0x621202 (613 floats); another version
/// needs the same calibration before the numbers can be trusted.
///
///   -5 -> -25, core 11, one Prime95 worker, medians of 438 samples:
///   V 1.0832 -> 1.0675   GHz 5.005 -> 5.167   W 14.02 -> 13.99   T 72.8 -> 72.7
/// </summary>
public sealed class PmTable
{
    private const int PowerBase = 301;
    private const int VoltBase = 317;
    private const int TempBase = 333;
    private const int FreqBase = 349;

    private readonly Cpu _cpu;

    public PmTable(Cpu cpu)
    {
        _cpu = cpu;
        IsAvailable = cpu.RefreshPowerTable() == SMU.Status.OK && cpu.powerTable?.Table is { Length: > FreqBase + 16 };
    }

    public bool IsAvailable { get; }
    public uint Version => _cpu.smu.TableVersion;
    public int Length => _cpu.powerTable?.Table?.Length ?? 0;

    /// <summary>Refreshes the table. False if the SMU does not answer.</summary>
    public bool Refresh() => IsAvailable && _cpu.RefreshPowerTable() == SMU.Status.OK;

    public float[] Raw => (float[])_cpu.powerTable.Table.Clone();

    public PmCoreSample Core(int core)
    {
        if (!IsAvailable || core is < 0 or > 15) return default;
        var t = _cpu.powerTable.Table;
        return new PmCoreSample(t[VoltBase + core], t[FreqBase + core], t[PowerBase + core], t[TempBase + core]);
    }
}
