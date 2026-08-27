using System.Runtime.InteropServices;

namespace LegionCoLab.Core;

public sealed class SafetyViolationException(string message) : Exception(message);

/// <summary>
/// Topes duros. Ninguno lleva bandera para saltarselo: si un valor no pasa por
/// aqui, no llega al buzon SMU.
/// </summary>
public static class Safety
{
    /// <summary>
    /// Nada mas agresivo que esto, pase lo que pase.
    /// -30 hasta el 27/08/2026; subido a -40 por decision del usuario tras
    /// pasar los nucleos 0 y 11 a -30 en cuatro regimenes, incluido fMax
    /// (docs/RESULTADOS.md). CoreCycler admite -50 en Ryzen 7000+.
    /// </summary>
    public const int AbsoluteMinMargin = -40;

    /// <summary>Un margen positivo SUBE el voltaje. Nunca es lo que queremos.</summary>
    public const int AbsoluteMaxMargin = 0;

    /// <summary>Salto maximo entre dos niveles consecutivos de una campana.</summary>
    public const int MaxStepBetweenLevels = 3;

    public static void ValidateMargin(int margin, string what = "margen")
    {
        if (margin > AbsoluteMaxMargin)
            throw new SafetyViolationException(
                $"{what} {margin:+#;-#;0} es positivo: eso SUBE el voltaje. Rechazado.");

        if (margin < AbsoluteMinMargin)
            throw new SafetyViolationException(
                $"{what} {margin} supera el tope absoluto ({AbsoluteMinMargin}). Rechazado.");
    }

    public static void ValidateMargins(IReadOnlyList<int> margins)
    {
        for (var i = 0; i < margins.Count; i++)
            ValidateMargin(margins[i], $"nucleo {i}: margen");
    }

    public static void ValidateStep(int from, int to)
    {
        var delta = Math.Abs(to - from);
        if (delta > MaxStepBetweenLevels)
            throw new SafetyViolationException(
                $"salto de {from} a {to} son {delta} cuentas; el maximo es {MaxStepBetweenLevels}.");
    }

    // ---- corriente alterna ----

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>1 = enchufado. Legion Toolkit se niega a aplicar el perfil con bateria.</summary>
    public static bool IsOnAcPower()
        => GetSystemPowerStatus(out var s) && s.ACLineStatus == 1;

    public static void RequireAcPower()
    {
        if (!IsOnAcPower())
            throw new SafetyViolationException(
                "no hay corriente alterna. Aplicar Curve Optimizer con bateria no esta soportado.");
    }
}
