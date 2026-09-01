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
        Topology.Map = CoreMap.From(_cpu);
        Safety.MinMargin = Safety.MinMarginFor(_cpu.info.codeName);
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

    /// <summary>
    /// Mobile APUs below Ryzen 9 read their margins but refuse every write
    /// (Ryzen 7 5800H verified 2026-08-28; RyzenAdj issue #233). A guess from
    /// the name; <see cref="WriteTest"/> settles it.
    /// </summary>
    public bool LikelyLocked => Topology.IsApu(_cpu) && !CpuName.Contains("Ryzen 9", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes a core's current margin back to itself and reports what the SMU
    /// answered. Nothing changes if it works; nothing changes if it does not.
    /// </summary>
    public string WriteTest(int coreIndex)
    {
        var now = ReadCore(coreIndex);
        if (now is null) return $"core {coreIndex} is not readable";
        var status = SendSet(Map.WriteMask(coreIndex), now.Value, _marginBits, out var where, out var arg);
        if (status == SMU.Status.FAILED && Topology.IsApu(_cpu) && _marginBits == 16)
        {
            var status20 = SendSet(Map.WriteMask(coreIndex), now.Value, 20, out _, out var arg20);
            if (status20 == SMU.Status.OK) { _marginBits = 20; return $"OK ({where}, 20-bit margin, arg 0x{arg20:X8})"; }
            return $"{status} ({where}, arg 0x{arg:X8}; 20-bit arg 0x{arg20:X8}: {status20}) - writes are LOCKED on this CPU";
        }
        return status == SMU.Status.OK ? $"OK ({where}, arg 0x{arg:X8})" : $"{status} ({where}, arg 0x{arg:X8}) - writes are LOCKED on this CPU";
    }

    public Cpu Cpu => _cpu;

    public string CpuName => _cpu.info.cpuName?.Trim() ?? "unknown";
    public string CodeName => _cpu.info.codeName.ToString();
    public int PhysicalCores => (int)_cpu.info.topology.physicalCores;
    public string SmuType => _cpu.smu.SMU_TYPE.ToString();

    /// <summary>If the SMU does not expose this message, Curve Optimizer cannot be applied.</summary>
    public bool IsPsmSupported => _cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin != 0;

    /// <summary>The core map read from the fuses (see <see cref="CoreMap"/>); also the process-wide <see cref="Topology.Map"/>.</summary>
    public CoreMap Map => Topology.Map;

    public int CoreCount => Math.Min(Map.Count, Topology.MaxCores);

    /// <summary>Why the map is not trusted, or null. install and find refuse on it.</summary>
    public string? TopologyWarning => Map.Warning;

    /// <summary>
    /// The map against the hardware: every mapped core must answer a margin
    /// read, and a slot the fuses call off should not. The first is a hard
    /// problem; the second only a note, since what the SMU does with a
    /// fused-off slot has not been seen on any machine yet.
    /// </summary>
    public (List<string> Problems, List<string> Notes) CheckMap()
    {
        var problems = new List<string>();
        var notes = new List<string>();
        var unreadable = ReadAll().Where(r => !r.IsReadable).Select(r => r.Index).ToList();
        if (unreadable.Count > 0) problems.Add($"cores {string.Join(",", unreadable)} are in the map but the SMU does not answer a margin read for them");
        if (!Map.Apu)
            foreach (var (ccd, slot) in Map.DisabledSlots().Take(4))
            {
                uint? v;
                try { v = _cpu.GetPsmMarginSingleCore(CoreMap.CcdMask(ccd, slot)); } catch { v = null; }
                if (v is not null) notes.Add($"CCD{ccd} slot {slot} is fused off by the map but the SMU answers {(int)v.Value} for it");
            }
        return (problems, notes);
    }

    public uint? TryGetFMax()
    {
        try { return _cpu.GetFMax(); }
        catch { return null; }
    }

    // ---- read ----

    public int? ReadCore(int coreIndex)
    {
        var raw = _cpu.GetPsmMarginSingleCore(Map.ReadMask(coreIndex));
        return raw.HasValue ? (int)raw.Value : null;   // same cast Legion Toolkit does
    }

    public IReadOnlyList<CoreReading> ReadAll()
    {
        var list = new List<CoreReading>(CoreCount);
        for (var i = 0; i < CoreCount; i++)
        {
            var mask = Map.ReadMask(i);
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
        if (!IsPsmSupported)
            throw new CoWriteFailedException("this SMU does not support SetDldoPsmMargin.");

        var mask = Map.WriteMask(coreIndex);

        var status = SendSet(mask, margin, _marginBits, out var where, out var arg);
        if (status == SMU.Status.FAILED && _marginBits == 16 && Topology.IsApu(_cpu))
        {
            // UXTU encodes negative margins in 20 bits (0x100000 - |m|) on APUs; ZenStates uses 16.
            var status20 = SendSet(mask, margin, 20, out var where20, out var arg20);
            if (status20 == SMU.Status.OK) { _marginBits = 20; status = status20; }
            else throw new CoWriteFailedException($"core {coreIndex}: the SMU rejected the write ({where}, arg 0x{arg:X8}): {status}; with a 20-bit margin ({where20}, arg 0x{arg20:X8}): {status20}. " +
                                                  "On mobile APUs AMD enables Curve Optimizer only on Ryzen 9 parts (RyzenAdj issue #233); a Ryzen 5/7 APU answers FAILED to every write.");
        }
        if (status != SMU.Status.OK)
            throw new CoWriteFailedException($"core {coreIndex}: the SMU rejected the write ({where}, arg 0x{arg:X8}): {status}.");

        var readback = ReadCore(coreIndex);
        if (readback != margin)
            throw new CoWriteFailedException(
                $"core {coreIndex}: wrote {margin} but the hardware reports " +
                $"{(readback.HasValue ? readback.Value.ToString() : "nothing")}.");
    }

    /// <summary>
    /// The same bytes ZenStates' SetPsmMarginSingleCore sends ((mask &amp; 0xFFF00000) |
    /// 16-bit margin, MP1 if its message id is set, else RSMU), but returning the
    /// SMU status instead of a bool so a rejection can be told apart from a
    /// missing command or a prerequisite.
    /// </summary>
    private int _marginBits = 16;

    /// <summary>Margin field width the SMU accepted: 16 (ZenStates) or 20 (UXTU on APUs).</summary>
    public int MarginBits => _marginBits;

    private SMU.Status SendSet(uint mask, int margin, int bits, out string where, out uint arg)
    {
        var m = bits == 20 ? (uint)(margin & 0xFFFFF) : Utils.MakePsmMarginArg(margin);
        arg = (mask & 0xFFF00000u) | m;
        var args = Utils.MakeCmdArgs(arg, 6);
        var mp1 = _cpu.smu.Mp1Smu.SMU_MSG_SetDldoPsmMargin;
        if (mp1 != 0)
        {
            where = $"MP1 0x{mp1:X}";
            return _cpu.smu.SendMp1Command(mp1, ref args);
        }
        var rsmu = _cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin;
        where = $"RSMU 0x{rsmu:X}";
        return _cpu.smu.SendRsmuCommand(rsmu, ref args);
    }

    /// <summary>
    /// Write without checking AC power or reading back. ONLY for the panic path,
    /// where the goal is to return to a known value and there is no time or
    /// guarantee for anything else. The margin limits still apply.
    /// </summary>
    public void WriteCoreUnchecked(int coreIndex, int margin)
    {
        Safety.ValidateMargin(margin, $"core {coreIndex}: margin");
        _cpu.SetPsmMarginSingleCore(Map.WriteMask(coreIndex), margin);
    }

    /// <summary>Writes every core and returns the verification read.</summary>
    public IReadOnlyList<CoreReading> WriteAll(IReadOnlyList<int> margins)
    {
        Safety.ValidateMargins(margins);
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
                if (_cpu.SetPsmMarginSingleCore(Map.WriteMask(i), baselineMargin))
                    ok++;
            }
            catch { /* in panic mode, keep going with the next core */ }
        }
        return ok;
    }

    public void Dispose() => _cpu.Dispose();
}
