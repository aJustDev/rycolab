using ZenStates.Core;

namespace Rycolab.Core;

/// <summary>
/// Core numbering helpers over the current <see cref="CoreMap"/>: CCD of a
/// core, its logical processor, the names. The map comes from the CPU when
/// a <see cref="CoController"/> opens the hardware; without hardware
/// (report, tests) it is the reference machine's uniform layout.
/// </summary>
public static class Topology
{
    /// <summary>Cores per CCD on Zen 3 and later (one CCX of eight); the uniform default and the fallback for indices past the map.</summary>
    public const int CoresPerCcd = 8;
    /// <summary>Profile slots. More cores than this are not handled (Threadripper).</summary>
    public const int MaxCores = 16;

    public static CoreMap Map { get; set; } = CoreMap.Uniform(MaxCores);

    /// <summary>First logical processor of a core (core N is logical N x threads: 2N with SMT on).</summary>
    public static int LogicalProcessor(int coreIndex) => Map.OsLogical(coreIndex);

    /// <summary>Affinity mask to pin a thread to the first logical of a physical core.</summary>
    public static long AffinityMask(int coreIndex) => 1L << LogicalProcessor(coreIndex);

    public static int CcdOf(int coreIndex) => Map.Ccd(coreIndex);

    /// <summary>
    /// CCD name, 0-based like Legion Toolkit ("CCD0", "CCD1") and like the SMU
    /// mask encoding itself.
    ///
    /// CAREFUL: HWiNFO and LibreHardwareMonitor count from 1. Our CCD0 is their
    /// "CCD1 (Tdie)" sensor. The translation lives in <see cref="CcdTempSensor"/>
    /// and nowhere else.
    /// </summary>
    public static string CcdName(int coreIndex) => CcdNameFromIndex(CcdOf(coreIndex));

    public static string CcdNameFromIndex(int ccdIndex) => $"CCD{ccdIndex}";

    /// <summary>The only place where our numbering is translated to LibreHardwareMonitor's.</summary>
    public static string CcdTempSensor(int ccdIndex) => $"CCD{ccdIndex + 1} (Tdie)";

    public static bool IsApu(Cpu cpu) => cpu.smu.SMU_TYPE is >= SMU.SmuType.TYPE_APU0 and <= SMU.SmuType.TYPE_APU2;
}
