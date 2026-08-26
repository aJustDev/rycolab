using ZenStates.Core;

namespace LegionCoLab.Core;

/// <summary>
/// Mapa de nucleos a CCD y codificacion de la mascara que espera el buzon SMU.
///
/// La codificacion replica AmdOverclockingController.EncodeCoreMarginBitmask de
/// Lenovo Legion Toolkit (GPL-3.0). Ver NOTICE.
/// </summary>
public static class Topology
{
    public const int CoresPerCcd = 8;
    public const int MaxCores = 16;

    /// <summary>Cada nucleo fisico ocupa dos procesadores logicos (SMT): el nucleo N es el logico 2N.</summary>
    public static int LogicalProcessor(int coreIndex) => coreIndex * 2;

    /// <summary>Mascara de afinidad para clavar un hilo al primer logico de un nucleo fisico.</summary>
    public static long AffinityMask(int coreIndex) => 1L << LogicalProcessor(coreIndex);

    public static int CcdOf(int coreIndex) => coreIndex / CoresPerCcd;

    /// <summary>Nombre legible del CCD, 1-indexado como en HWiNFO.</summary>
    public static string CcdName(int coreIndex) => $"CCD{CcdOf(coreIndex) + 1}";

    public static uint CoreMask(Cpu cpu, int coreIndex)
    {
        if (cpu.smu.SMU_TYPE is >= SMU.SmuType.TYPE_APU0 and <= SMU.SmuType.TYPE_APU2)
            return (uint)coreIndex;

        var ccd = coreIndex / CoresPerCcd;
        var local = coreIndex % CoresPerCcd;
        return (uint)(((ccd << 8) | local) << 20);
    }
}
