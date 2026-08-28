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

    public static uint CoreMask(Cpu cpu, int coreIndex)
    {
        if (cpu.smu.SMU_TYPE is >= SMU.SmuType.TYPE_APU0 and <= SMU.SmuType.TYPE_APU2)
            return (uint)coreIndex;

        var ccd = coreIndex / CoresPerCcd;
        var local = coreIndex % CoresPerCcd;
        return (uint)(((ccd << 8) | local) << 20);
    }
}
