using ZenStates.Core;

namespace LegionCoLab.Core;

public readonly record struct PmCoreSample(double? Volt, double? Freq, double? Power, double? Temp);

/// <summary>
/// Lectura por nucleo de la tabla de potencia del SMU (floats crudos que
/// expone ZenStates). LibreHardwareMonitor no da tension por nucleo en este
/// chip, la tabla si.
///
/// Los indices se localizaron EMPIRICAMENTE el 27/08/2026 (9955HX3D) por
/// diferencia entre dos margenes con un solo nucleo cargado (scripts/pm-diff.ps1):
/// cuatro bloques de 16 floats consecutivos donde solo se movia la posicion
/// base+11 con el nucleo 11 bajo carga, y cuyos valores coinciden con los de
/// LibreHardwareMonitor (potencia, temperatura, reloj). Verificados con la
/// tabla version 0x621202 (613 floats); con otra version hay que repetir
/// la localizacion antes de fiarse.
///
///   -5 -> -25, nucleo 11, un trabajador de Prime95, medianas de 438 muestras:
///   V 1,0832 -> 1,0675   GHz 5,005 -> 5,167   W 14,02 -> 13,99   T 72,8 -> 72,7
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

    /// <summary>Refresca la tabla. Devuelve false si el SMU no responde.</summary>
    public bool Refresh() => IsAvailable && _cpu.RefreshPowerTable() == SMU.Status.OK;

    public float[] Raw => (float[])_cpu.powerTable.Table.Clone();

    public PmCoreSample Core(int core)
    {
        if (!IsAvailable || core is < 0 or > 15) return default;
        var t = _cpu.powerTable.Table;
        return new PmCoreSample(t[VoltBase + core], t[FreqBase + core], t[PowerBase + core], t[TempBase + core]);
    }
}
