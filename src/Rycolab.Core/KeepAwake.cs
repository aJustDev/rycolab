using System.Runtime.InteropServices;

namespace Rycolab.Core;

/// <summary>
/// Holds the system awake while a campaign runs. A pinned y-cruncher load is
/// not user activity, so Windows' idle timer fires mid-run and the sleep
/// silently resets every Curve Optimizer margin to the BIOS baseline.
/// Display may still turn off; only sleep is held.
/// </summary>
public static class KeepAwake
{
    private const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;

    public static void On() => SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
    public static void Off() => SetThreadExecutionState(ES_CONTINUOUS);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);
}
