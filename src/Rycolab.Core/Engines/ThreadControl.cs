using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Rycolab.Core.Engines;

/// <summary>
/// Periodic suspension of every thread of a process (CoreCycler pattern,
/// script-corecycler.ps1:1813-1818): 1 s stopped every 10 s to force
/// idle-to-boost transitions, which is where Curve Optimizer breaks.
/// </summary>
internal static class ThreadControl
{
    private const uint THREAD_SUSPEND_RESUME = 0x0002;

    [DllImport("kernel32.dll")] private static extern IntPtr OpenThread(uint access, bool inherit, uint tid);
    [DllImport("kernel32.dll")] private static extern int SuspendThread(IntPtr h);
    [DllImport("kernel32.dll")] private static extern int ResumeThread(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    public static int Suspend(int pid) => Apply(pid, true);
    public static int Resume(int pid) => Apply(pid, false);

    private static int Apply(int pid, bool suspend)
    {
        var n = 0;
        foreach (ProcessThread t in Process.GetProcessById(pid).Threads)
        {
            var h = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)t.Id);
            if (h == IntPtr.Zero) continue;
            var r = suspend ? SuspendThread(h) : ResumeThread(h);
            if (r >= 0) n++;
            CloseHandle(h);
        }
        return n;
    }
}
