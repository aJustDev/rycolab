using ZenStates.Core;

namespace LegionCoLab.Core;

public readonly record struct CoreReading(int Index, string Ccd, uint Mask, int? Margin)
{
    public bool IsReadable => Margin.HasValue;
}

public sealed class CoWriteFailedException(string message) : Exception(message);

/// <summary>
/// Lectura y escritura de margenes PSM (Curve Optimizer) por nucleo, contra el
/// buzon SMU a traves de ZenStates.Core.
///
/// Regla de la casa: toda escritura se relee. Si no coincide, es un fallo duro.
/// </summary>
public sealed class CoController : IDisposable
{
    private readonly Cpu _cpu;

    public CoController()
    {
        _cpu = new Cpu();
    }

    public Cpu Cpu => _cpu;

    public string CpuName => _cpu.info.cpuName?.Trim() ?? "desconocido";
    public int PhysicalCores => (int)_cpu.info.topology.physicalCores;
    public string SmuType => _cpu.smu.SMU_TYPE.ToString();

    /// <summary>Si el SMU no expone este mensaje, el Curve Optimizer no puede aplicarse.</summary>
    public bool IsPsmSupported => _cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin != 0;

    public int CoreCount => Math.Min(PhysicalCores, Topology.MaxCores);

    public uint? TryGetFMax()
    {
        try { return _cpu.GetFMax(); }
        catch { return null; }
    }

    // ---- lectura ----

    public int? ReadCore(int coreIndex)
    {
        var raw = _cpu.GetPsmMarginSingleCore(Topology.CoreMask(_cpu, coreIndex));
        return raw.HasValue ? (int)raw.Value : null;   // mismo cast que hace Legion Toolkit
    }

    public IReadOnlyList<CoreReading> ReadAll()
    {
        var list = new List<CoreReading>(CoreCount);
        for (var i = 0; i < CoreCount; i++)
        {
            var mask = Topology.CoreMask(_cpu, i);
            int? margin;
            try { margin = ReadCore(i); }
            catch { margin = null; }

            list.Add(new CoreReading(i, Topology.CcdName(i), mask, margin));
        }
        return list;
    }

    /// <summary>Un nucleo se considera activo si su margen se puede leer, igual que hace LLT.</summary>
    public bool IsCoreActive(int coreIndex)
    {
        try { return ReadCore(coreIndex).HasValue; }
        catch { return false; }
    }

    // ---- escritura ----

    /// <summary>
    /// Escribe el margen de un nucleo y lo relee para confirmarlo.
    /// El valor es ABSOLUTO: escribir -8 deja -8, no se suma a lo que hubiera.
    /// </summary>
    public void WriteCore(int coreIndex, int margin)
    {
        Safety.ValidateMargin(margin, $"nucleo {coreIndex}: margen");
        Safety.RequireAcPower();

        if (!IsPsmSupported)
            throw new CoWriteFailedException("este SMU no soporta SetDldoPsmMargin.");

        var mask = Topology.CoreMask(_cpu, coreIndex);

        if (!_cpu.SetPsmMarginSingleCore(mask, margin))
            throw new CoWriteFailedException($"nucleo {coreIndex}: el SMU rechazo la escritura.");

        var readback = ReadCore(coreIndex);
        if (readback != margin)
            throw new CoWriteFailedException(
                $"nucleo {coreIndex}: se escribio {margin} pero el hardware devuelve " +
                $"{(readback.HasValue ? readback.Value.ToString() : "nada")}.");
    }

    /// <summary>Escribe los 16 nucleos y devuelve la lectura de verificacion.</summary>
    public IReadOnlyList<CoreReading> WriteAll(IReadOnlyList<int> margins)
    {
        Safety.ValidateMargins(margins);
        Safety.RequireAcPower();

        for (var i = 0; i < CoreCount && i < margins.Count; i++)
        {
            if (!IsCoreActive(i)) continue;
            WriteCore(i, margins[i]);
        }

        return ReadAll();
    }

    public IReadOnlyList<CoreReading> WriteUniform(int margin)
        => WriteAll(Enumerable.Repeat(margin, CoreCount).ToArray());

    /// <summary>
    /// Vuelta a la base. Se usa tambien desde el manejador de panico, asi que no
    /// lanza: hace lo que puede y devuelve cuantos nucleos logro restaurar.
    /// </summary>
    public int TryRestore(int baselineMargin)
    {
        var ok = 0;
        for (var i = 0; i < CoreCount; i++)
        {
            try
            {
                if (!IsCoreActive(i)) continue;
                if (_cpu.SetPsmMarginSingleCore(Topology.CoreMask(_cpu, i), baselineMargin))
                    ok++;
            }
            catch { /* en panico se sigue con el siguiente */ }
        }
        return ok;
    }

    public void Dispose() => _cpu.Dispose();
}
