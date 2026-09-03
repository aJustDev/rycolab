using System.Runtime.InteropServices;

namespace Rycolab.Core;

/// <summary>
/// Seconds since the user last touched the keyboard or mouse
/// (GetLastInputInfo): tells "on battery, in use" from "on battery,
/// forgotten". Null when the call fails or the value is not plausible
/// (a non-interactive session reports nothing useful).
/// </summary>
public static class UserIdle
{
    public static int? Seconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info) || info.dwTime == 0) return null;
        var ms = unchecked((uint)Environment.TickCount - info.dwTime);
        if (ms > Environment.TickCount64) return null;
        return (int)Math.Min(ms / 1000, int.MaxValue);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
