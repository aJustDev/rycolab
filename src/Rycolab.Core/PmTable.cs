using System.Text.Json;
using ZenStates.Core;

namespace Rycolab.Core;

public readonly record struct PmCoreSample(double? Volt, double? Freq, double? Power, double? Temp);

/// <summary>Where the four per-core blocks start in a given PM table version, plus the package-power scalar.</summary>
public sealed record PmIndex(int Power, int Volt, int Temp, int Freq, int? Pkg = null)
{
    /// <summary>
    /// Ryzen 9 9955HX3D, table version 0x621202 (613 floats), blocks located
    /// empirically on 2026-08-27; package scalar (offset 3, mirrored at 26)
    /// on 2026-09-01: the true package incl. the IO die (DC idle ~10 W,
    /// 16-core load ~111 W = core domain 96 + SoC 15). Offset 20 is the core
    /// domain only (near zero at idle) and 390 a slow STAPM-like average.
    /// </summary>
    public static readonly PmIndex Known621202 = new(301, 317, 333, 349, 3);

    public static string File => Path.Combine(AppPaths.Data, "pm-index.json");

    public static PmIndex? For(uint version)
    {
        var all = Journal.ReadJsonFile<Dictionary<string, PmIndex>>(File);
        if (all is not null && all.TryGetValue(Key(version), out var idx)) return idx;
        return version == 0x621202 ? Known621202 : null;
    }

    public static void Save(uint version, PmIndex idx)
    {
        var all = Journal.ReadJsonFile<Dictionary<string, PmIndex>>(File) ?? [];
        all[Key(version)] = idx;
        Journal.WriteJsonFile(File, all);
    }

    public static string Key(uint version) => $"0x{version:X}";
}

/// <summary>
/// Per-core reads from the SMU power-management table (the raw floats
/// ZenStates exposes). LibreHardwareMonitor gives no per-core voltage on
/// these chips; the table does.
///
/// The per-core blocks are 16 consecutive floats each (power, voltage,
/// temperature, frequency). Their positions depend on the table version and
/// are located by `rycolab dev calibrate` (one loaded core stands out from
/// the other fifteen in each block); the reference machine's are built in.
/// With an unknown version the per-core telemetry is simply unavailable.
/// </summary>
public sealed class PmTable
{
    private readonly Cpu _cpu;
    private readonly PmIndex? _idx;

    public PmTable(Cpu cpu)
    {
        _cpu = cpu;
        var ok = cpu.RefreshPowerTable() == SMU.Status.OK && cpu.powerTable?.Table is { Length: > 0 };
        _idx = ok ? PmIndex.For(cpu.smu.TableVersion) : null;
        IsAvailable = ok && _idx is not null && cpu.powerTable!.Table.Length > _idx.Freq + Topology.MaxCores;
        HasTable = ok;
    }

    /// <summary>The SMU answers and the table is readable (even if the per-core indices are unknown).</summary>
    public bool HasTable { get; }
    /// <summary>Per-core values can be read.</summary>
    public bool IsAvailable { get; }
    public uint Version => _cpu.smu.TableVersion;
    public int Length => _cpu.powerTable?.Table?.Length ?? 0;
    public PmIndex? Index => _idx;

    /// <summary>Refreshes the table. False if the SMU does not answer.</summary>
    public bool Refresh() => HasTable && _cpu.RefreshPowerTable() == SMU.Status.OK;

    public float[] Raw => (float[])_cpu.powerTable.Table.Clone();

    public PmCoreSample Core(int core)
    {
        if (!IsAvailable || core is < 0 or >= Topology.MaxCores) return default;
        var t = _cpu.powerTable.Table;
        return new PmCoreSample(t[_idx!.Volt + core], t[_idx.Freq + core], t[_idx.Power + core], t[_idx.Temp + core]);
    }

    /// <summary>
    /// Package power in W, the float the SMU computes itself. The reliable
    /// alternative to LibreHardwareMonitor's RAPL energy-counter delta, which
    /// intermittently returns garbage (150-270 W at idle seen on 2026-09-01).
    /// Null when the offset is unknown for this table version or the value is
    /// implausible. The caller refreshes, like <see cref="Core"/>.
    /// </summary>
    public double? Package()
    {
        if (!HasTable || _idx?.Pkg is not { } p || p >= Length) return null;
        double v = _cpu.powerTable.Table[p];
        return v is > 0 and < 250 ? v : null;
    }
}
