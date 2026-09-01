using System.Runtime.InteropServices;

namespace Rycolab.Core;

/// <summary>
/// Total CPU load between two calls, from the kernel's own counters
/// (GetSystemTimes): no driver and no LibreHardwareMonitor in the process
/// that runs for days. The first call primes and returns null.
/// </summary>
public sealed class CpuLoad
{
    private ulong _idle, _total;
    private bool _primed;

    public double? Percent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var i = Ticks(idle);
        var total = Ticks(kernel) + Ticks(user);   // kernel time includes idle time
        double? result = null;
        if (_primed && total > _total)
            result = Math.Clamp(100.0 * (1 - (double)(i - _idle) / (total - _total)), 0, 100);
        _idle = i;
        _total = total;
        _primed = true;
        return result;
    }

    private static ulong Ticks(FILETIME t) => ((ulong)t.High << 32) | t.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low; public uint High; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
}
