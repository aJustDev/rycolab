using ZenStates.Core;

namespace Rycolab.Core;

/// <summary>One enabled core: its index in rycolab (0..N-1, the OS order), its CCD, its physical slot in that CCD (what the SMU mask wants) and its first logical processor.</summary>
public sealed record CoreInfo(int Index, int Ccd, int Physical, int OsLogical);

/// <summary>
/// The core map: which enabled cores the CPU has, where each one sits (CCD
/// and physical slot) and which logical processor runs it. Derived from the
/// fuses as ZenStates reads them (<see cref="From(Cpu)"/>): the SMU mask
/// wants the physical slot within the CCD, which is not the OS index once a
/// core is fused off (a 7900X is two CCDs of six out of eight). Without
/// hardware (report, tests) the map is the uniform 8-per-CCD layout of the
/// reference machine (<see cref="Uniform"/>).
///
/// The mask encoding, ((ccd &lt;&lt; 8) | slot) &lt;&lt; 20, is ZenStates'
/// MakeCoreMask with one CCX per CCD (Zen 3 and later; Zen 2 has no per-core
/// Curve Optimizer anyway) and Legion Toolkit's EncodeCoreMarginBitmask.
/// APUs read with the plain index and write with it at bit 20.
/// </summary>
public sealed class CoreMap
{
    private readonly CoreInfo[] _cores;

    public IReadOnlyList<CoreInfo> Cores => _cores;
    public int Count => _cores.Length;
    public int Ccds { get; }
    public bool Apu { get; }
    public int ThreadsPerCore { get; }
    /// <summary>Per CCD, bit n set = physical slot n fused off.</summary>
    public IReadOnlyList<uint> Disabled { get; }
    /// <summary>Null when the map is trusted. Otherwise why not: the fuses and CPUID disagree, or the topology could not be read and the uniform layout is assumed.</summary>
    public string? Warning { get; }

    private CoreMap(CoreInfo[] cores, int ccds, bool apu, int threadsPerCore, uint[] disabled, string? warning)
    {
        _cores = cores; Ccds = ccds; Apu = apu; ThreadsPerCore = threadsPerCore; Disabled = disabled; Warning = warning;
    }

    public static CoreMap Uniform(int cores, int coresPerCcd = Topology.CoresPerCcd, int threadsPerCore = 2, bool apu = false, string? warning = null)
    {
        cores = Math.Max(1, cores);
        var list = Enumerable.Range(0, cores).Select(i => new CoreInfo(i, i / coresPerCcd, i % coresPerCcd, i * threadsPerCore)).ToArray();
        var ccds = (cores + coresPerCcd - 1) / coresPerCcd;
        return new CoreMap(list, ccds, apu, threadsPerCore, new uint[ccds], warning);
    }

    /// <summary>
    /// From the fuses: CCD count, cores per CCD, the disable map of each CCD
    /// (bit n = physical slot n off), threads per core, and the enabled core
    /// count CPUID reports. Enabled slots are numbered contiguously across
    /// CCDs (the OS order); core N runs on logical N x threads. When the
    /// fuses and CPUID disagree the map is not trusted: the uniform layout
    /// comes back with a warning, and install / find refuse on it.
    /// </summary>
    public static CoreMap From(int ccds, int coresPerCcd, IReadOnlyList<uint> coreDisableMap, int threadsPerCore, bool apu, int enabledCores)
    {
        threadsPerCore = Math.Max(1, threadsPerCore);
        if (ccds <= 0 || coresPerCcd <= 0 || enabledCores <= 0)
            return Uniform(enabledCores, threadsPerCore: threadsPerCore, apu: apu,
                warning: "the CPU topology could not be read (ccds, cores per CCD or core count is zero); assuming 8 cores per CCD in order");

        var list = new List<CoreInfo>();
        var disabled = new uint[ccds];
        for (var ccd = 0; ccd < ccds; ccd++)
        {
            disabled[ccd] = ccd < coreDisableMap.Count ? coreDisableMap[ccd] : 0;
            for (var slot = 0; slot < coresPerCcd; slot++)
            {
                if (((disabled[ccd] >> slot) & 1) != 0) continue;
                list.Add(new CoreInfo(list.Count, ccd, slot, list.Count * threadsPerCore));
            }
        }
        if (list.Count != enabledCores)
            return Uniform(enabledCores, threadsPerCore: threadsPerCore, apu: apu,
                warning: $"the fuses describe {list.Count} cores ({Layout(list, ccds)}) but CPUID reports {enabledCores}; the per-core map cannot be trusted");
        return new CoreMap(list.ToArray(), ccds, apu, threadsPerCore, disabled, null);
    }

    public static CoreMap From(Cpu cpu)
    {
        var t = cpu.info.topology;
        var ccds = (int)t.ccds;
        // ccxs is the CCX count; one per CCD on Zen 3 and later, two on Zen 2 (which has no per-core Curve Optimizer).
        var ccxPerCcd = ccds > 0 && t.ccxs >= t.ccds ? (int)(t.ccxs / t.ccds) : 1;
        return From(ccds, (int)t.coresPerCcx * Math.Max(1, ccxPerCcd), t.coreDisableMap ?? [], (int)t.threadsPerCore, Topology.IsApu(cpu), (int)t.physicalCores);
    }

    public CoreInfo this[int index] => _cores[index];

    /// <summary>Indices past the map (a 16-slot profile on an 8-core CPU) fall back to the uniform layout, for display only.</summary>
    public int Ccd(int index) => index < Count ? _cores[index].Ccd : index / Topology.CoresPerCcd;
    public int Physical(int index) => index < Count ? _cores[index].Physical : index % Topology.CoresPerCcd;
    public int OsLogical(int index) => index < Count ? _cores[index].OsLogical : index * ThreadsPerCore;

    public static uint CcdMask(int ccd, int physical) => (uint)(((ccd << 8) | physical) << 20);

    /// <summary>Mask for GetDldoPsmMargin: the plain index on APUs (Legion Toolkit, SMUDebugTool and ZenStates' APU overload agree), the CCD mask elsewhere.</summary>
    public uint ReadMask(int index) => Apu ? (uint)index : CcdMask(Ccd(index), Physical(index));

    /// <summary>Mask for SetDldoPsmMargin: ZenStates packs (mask &amp; 0xFFF00000) | margin, so on APUs the index goes to bit 20.</summary>
    public uint WriteMask(int index) => Apu ? (uint)index << 20 : CcdMask(Ccd(index), Physical(index));

    public IEnumerable<CoreInfo> CoresOfCcd(int ccd) => _cores.Where(c => c.Ccd == ccd);

    /// <summary>The fused-off slots, for a probe to confirm they do not answer.</summary>
    public IEnumerable<(int Ccd, int Physical)> DisabledSlots()
    {
        for (var ccd = 0; ccd < Ccds; ccd++)
            for (var slot = 0; slot < 8; slot++)
                if (((Disabled[ccd] >> slot) & 1) != 0) yield return (ccd, slot);
    }

    /// <summary>"2 CCDs, 8+8 cores, SMT on" or "2 CCDs, 6+6 cores (off: CCD0 6,7; CCD1 0,1), SMT on".</summary>
    public string Describe()
    {
        var off = DisabledSlots().GroupBy(s => s.Ccd).Select(g => $"CCD{g.Key} {string.Join(",", g.Select(s => s.Physical))}").ToList();
        return $"{Ccds} CCD{(Ccds == 1 ? "" : "s")}, {Layout(_cores, Ccds)} cores{(off.Count > 0 ? $" (off: {string.Join("; ", off)})" : "")}, " +
               $"{(ThreadsPerCore > 1 ? "SMT on" : "SMT off")}{(Apu ? ", APU" : "")}";
    }

    private static string Layout(IEnumerable<CoreInfo> cores, int ccds)
        => string.Join("+", Enumerable.Range(0, ccds).Select(ccd => cores.Count(c => c.Ccd == ccd)));
}
