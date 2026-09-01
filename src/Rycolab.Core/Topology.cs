using ZenStates.Core;

namespace Rycolab.Core;

/// <summary>
/// Core-to-CCD map and the core mask encoding the SMU mailbox expects.
///
/// The encoding replicates AmdOverclockingController.EncodeCoreMarginBitmask
/// from Lenovo Legion Toolkit (GPL-3.0). See NOTICE.
/// </summary>
public static class Topology
{
    public const int CoresPerCcd = 8;
    public const int MaxCores = 16;

    /// <summary>Each physical core owns two logical processors (SMT): core N is logical 2N.</summary>
    public static int LogicalProcessor(int coreIndex) => coreIndex * 2;

    /// <summary>Affinity mask to pin a thread to the first logical of a physical core.</summary>
    public static long AffinityMask(int coreIndex) => 1L << LogicalProcessor(coreIndex);

    public static int CcdOf(int coreIndex) => coreIndex / CoresPerCcd;

    /// <summary>
    /// CCD name, 0-based like Legion Toolkit ("CCD 0", "CCD 1") and like the SMU
    /// mask encoding itself.
    ///
    /// CAREFUL: HWiNFO and LibreHardwareMonitor count from 1. Our CCD0 is their
    /// "CCD1 (Tdie)" sensor. The translation lives in <see cref="CcdTempSensor"/>
    /// and nowhere else.
    /// </summary>
    public static string CcdName(int coreIndex) => $"CCD{CcdOf(coreIndex)}";

    public static string CcdNameFromIndex(int ccdIndex) => $"CCD{ccdIndex}";

    /// <summary>The only place where our numbering is translated to LibreHardwareMonitor's.</summary>
    public static string CcdTempSensor(int ccdIndex) => $"CCD{ccdIndex + 1} (Tdie)";

    public static int FirstCoreOfCcd(int ccdIndex) => ccdIndex * CoresPerCcd;

    public static bool IsApu(Cpu cpu) => cpu.smu.SMU_TYPE is >= SMU.SmuType.TYPE_APU0 and <= SMU.SmuType.TYPE_APU2;

    /// <summary>
    /// Mask for GetDldoPsmMargin. On APUs it is the plain core index (Legion
    /// Toolkit, SMUDebugTool and ZenStates.Core's own APU overload agree; read
    /// 8 of 8 on a Ryzen 7 5800H on 2026-08-28).
    /// </summary>
    public static uint ReadMask(Cpu cpu, int coreIndex) => ReadMask(IsApu(cpu), coreIndex);

    public static uint ReadMask(bool apu, int coreIndex)
        => apu ? (uint)coreIndex : CcdMask(coreIndex);

    /// <summary>
    /// Mask for SetDldoPsmMargin. ZenStates.Core packs the argument as
    /// (mask &amp; 0xFFF00000) | (margin &amp; 0xFFFF), so on APUs the core index
    /// must sit at bit 20: core &lt;&lt; 20, as ZenStates' MakeCoreMask and UXTU's
    /// ryzenadj argument do. The plain index Legion Toolkit uses would be
    /// masked out and every write would land on core 0.
    /// </summary>
    public static uint WriteMask(Cpu cpu, int coreIndex) => WriteMask(IsApu(cpu), coreIndex);

    public static uint WriteMask(bool apu, int coreIndex)
        => apu ? (uint)coreIndex << 20 : CcdMask(coreIndex);

    /// <summary>Legion Toolkit's EncodeCoreMarginBitmask for CPUs with CCDs.</summary>
    public static uint CcdMask(int coreIndex)
    {
        var ccd = coreIndex / CoresPerCcd;
        var local = coreIndex % CoresPerCcd;
        return (uint)(((ccd << 8) | local) << 20);
    }
}
