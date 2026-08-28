using ZenStates.Core;

namespace Rycolab.Core;

public readonly record struct CoreReading(int Index, string Ccd, uint Mask, int? Margin)
{
    public bool IsReadable => Margin.HasValue;
}

public sealed class CoWriteFailedException(string message) : Exception(message);

/// <summary>
/// Reads and writes per-core PSM margins (Curve Optimizer) through the SMU
/// mailbox via ZenStates.Core.
///
/// House rule: every write is read back. A mismatch is a hard failure.
/// </summary>
public sealed class CoController : IDisposable
{
    private readonly Cpu _cpu;

    public CoController()
    {
        _cpu = new Cpu();
        ApplyMailboxOverrides();
        if (WriteMailbox == "") WriteMailbox = $"RSMU 0x{_cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin:X}";
    }

    /// <summary>
    /// APUs take the per-core Curve Optimizer write on the MP1 mailbox, not on
    /// RSMU as ZenStates.Core assumes: ryzenadj (lib/api.c set_coper/set_coall)
    /// and UXTU (RyzenSmu.cs socket tables) send MP1 0x54/0x55 on Cezanne and
    /// MP1 0x4B/0x4C on Rembrandt/Phoenix/Hawk Point/Strix. ZenStates sends
    /// MP1 when the MP1 message id is set, so we fill it in. Reads stay on RSMU.
    /// Verified: Ryzen 7 5800H rejected RSMU 0x52 on 2026-08-28.
    /// </summary>
    private void ApplyMailboxOverrides()
    {
        if (!Topology.IsApu(_cpu) || _cpu.smu.Mp1Smu.SMU_MSG_SetDldoPsmMargin != 0) return;
        var (set, setAll) = _cpu.info.codeName switch
        {
            Cpu.CodeName.Cezanne => (0x54u, 0x55u),
            Cpu.CodeName.Rembrandt or Cpu.CodeName.Phoenix or Cpu.CodeName.Phoenix2
                or Cpu.CodeName.HawkPoint or Cpu.CodeName.StrixPoint => (0x4Bu, 0x4Cu),   // from ryzenadj/UXTU, untested here
            _ => (0u, 0u),
        };
        if (set == 0) return;
        _cpu.smu.Mp1Smu.SMU_MSG_SetDldoPsmMargin = set;
        _cpu.smu.Mp1Smu.SMU_MSG_SetAllDldoPsmMargin = setAll;
        WriteMailbox = $"MP1 0x{set:X}";
    }

    /// <summary>Where per-core writes go: "RSMU 0x.." (ZenStates default) or an MP1 override.</summary>
    public string WriteMailbox { get; private set; } = "";

    public Cpu Cpu => _cpu;

    public string CpuName => _cpu.info.cpuName?.Trim() ?? "unknown";
    public int PhysicalCores => (int)_cpu.info.topology.physicalCores;
    public string SmuType => _cpu.smu.SMU_TYPE.ToString();

    /// <summary>If the SMU does not expose this message, Curve Optimizer cannot be applied.</summary>
    public bool IsPsmSupported => _cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin != 0;

    public int CoreCount => Math.Min(PhysicalCores, Topology.MaxCores);

    public uint? TryGetFMax()
    {
        try { return _cpu.GetFMax(); }
        catch { return null; }
    }

    // ---- read ----

    public int? ReadCore(int coreIndex)
    {
        var raw = _cpu.GetPsmMarginSingleCore(Topology.ReadMask(_cpu, coreIndex));
        return raw.HasValue ? (int)raw.Value : null;   // same cast Legion Toolkit does
    }

    public IReadOnlyList<CoreReading> ReadAll()
    {
        var list = new List<CoreReading>(CoreCount);
        for (var i = 0; i < CoreCount; i++)
        {
            var mask = Topology.ReadMask(_cpu, i);
            int? margin;
            try { margin = ReadCore(i); }
            catch { margin = null; }

            list.Add(new CoreReading(i, Topology.CcdName(i), mask, margin));
        }
        return list;
    }

    /// <summary>A core counts as active if its margin can be read, as Legion Toolkit does.</summary>
    public bool IsCoreActive(int coreIndex)
    {
        try { return ReadCore(coreIndex).HasValue; }
        catch { return false; }
    }

    // ---- write ----

    /// <summary>
    /// Writes one core's margin and reads it back to confirm.
    /// The value is ABSOLUTE: writing -8 leaves -8, it is not added to what was there.
    /// </summary>
    public void WriteCore(int coreIndex, int margin)
    {
        Safety.ValidateMargin(margin, $"core {coreIndex}: margin");
        Safety.RequireAcPower();

        if (!IsPsmSupported)
            throw new CoWriteFailedException("this SMU does not support SetDldoPsmMargin.");

        var mask = Topology.WriteMask(_cpu, coreIndex);

        if (!_cpu.SetPsmMarginSingleCore(mask, margin))
            throw new CoWriteFailedException($"core {coreIndex}: the SMU rejected the write.");

        var readback = ReadCore(coreIndex);
        if (readback != margin)
            throw new CoWriteFailedException(
                $"core {coreIndex}: wrote {margin} but the hardware reports " +
                $"{(readback.HasValue ? readback.Value.ToString() : "nothing")}.");
    }

    /// <summary>
    /// Write without checking AC power or reading back. ONLY for the panic path,
    /// where the goal is to return to a known value and there is no time or
    /// guarantee for anything else. The margin limits still apply.
    /// </summary>
    public void WriteCoreUnchecked(int coreIndex, int margin)
    {
        Safety.ValidateMargin(margin, $"core {coreIndex}: margin");
        _cpu.SetPsmMarginSingleCore(Topology.WriteMask(_cpu, coreIndex), margin);
    }

    /// <summary>Writes every core and returns the verification read.</summary>
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
    /// Back to the baseline. Also used from the panic handler, so it does not
    /// throw: it does what it can and returns how many cores it restored.
    /// </summary>
    public int TryRestore(int baselineMargin)
    {
        var ok = 0;
        for (var i = 0; i < CoreCount; i++)
        {
            try
            {
                if (!IsCoreActive(i)) continue;
                if (_cpu.SetPsmMarginSingleCore(Topology.WriteMask(_cpu, i), baselineMargin))
                    ok++;
            }
            catch { /* in panic mode, keep going with the next core */ }
        }
        return ok;
    }

    public void Dispose() => _cpu.Dispose();
}
