using System.Runtime.InteropServices;

namespace Rycolab.Core;

public sealed class SafetyViolationException(string message) : Exception(message);

/// <summary>
/// Hard limits. None of them has a flag to bypass it: a value that does not
/// pass here never reaches the SMU mailbox.
/// </summary>
public static class Safety
{
    /// <summary>
    /// Nothing more aggressive than this, no matter what. -50 is the minimum
    /// the SMU accepts on Ryzen 7000 and later (CoreCycler default.config.ini:760);
    /// Ryzen 5000 stops at -30.
    /// </summary>
    public const int AbsoluteMinMargin = -50;

    /// <summary>A positive margin RAISES the voltage. Never what we want.</summary>
    public const int AbsoluteMaxMargin = 0;

    /// <summary>Largest jump between two consecutive levels of a walk.</summary>
    public const int MaxStepBetweenLevels = 3;

    public static void ValidateMargin(int margin, string what = "margin")
    {
        if (margin > AbsoluteMaxMargin)
            throw new SafetyViolationException(
                $"{what} {margin:+#;-#;0} is positive: that RAISES the voltage. Rejected.");

        if (margin < AbsoluteMinMargin)
            throw new SafetyViolationException(
                $"{what} {margin} is below the absolute limit ({AbsoluteMinMargin}). Rejected.");
    }

    public static void ValidateMargins(IReadOnlyList<int> margins)
    {
        for (var i = 0; i < margins.Count; i++)
            ValidateMargin(margins[i], $"core {i}: margin");
    }

    public static void ValidateStep(int from, int to)
    {
        var delta = Math.Abs(to - from);
        if (delta > MaxStepBetweenLevels)
            throw new SafetyViolationException(
                $"jump from {from} to {to} is {delta} counts; the maximum is {MaxStepBetweenLevels}.");
    }

    // ---- AC power ----

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

    /// <summary>1 = plugged in. The stress campaigns and `dev apply` insist on it; the guard keeps a validated profile on battery too.</summary>
    public static bool IsOnAcPower()
        => GetSystemPowerStatus(out var s) && s.ACLineStatus == 1;

    public static void RequireAcPower()
    {
        if (!IsOnAcPower())
            throw new SafetyViolationException(
                "not on AC power. Applying Curve Optimizer on battery is not supported.");
    }
}
