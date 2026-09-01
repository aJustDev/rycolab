using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Rycolab.Core.Legion;

/// <summary>
/// The Windows side of the battery profile: the internal panel's refresh
/// rate (a display mode change, frequency only), its brightness (the
/// monitor WMI class), the DC values of the active power scheme (powercfg)
/// and the power-mode slider (read only: Windows keeps one overlay per
/// line and switches it itself). Every setter returns the value read back.
/// </summary>
public static class WindowsPower
{
    // ---- refresh rate -------------------------------------------------

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DM_DISPLAYFREQUENCY = 0x400000;
    private const int CDS_UPDATEREGISTRY = 0x1;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? lpDevice, int iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, int dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

    /// <summary>The primary attached display (the internal panel when nothing else is primary).</summary>
    private static string? PrimaryDisplay()
    {
        var d = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (var i = 0; EnumDisplayDevices(null, i, ref d, 0); i++)
        {
            if ((d.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0 && (d.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0) return d.DeviceName;
            d = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        }
        return null;
    }

    private static DEVMODE? Current(string device)
    {
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        return EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref dm) ? dm : null;
    }

    public static int? RefreshHz => PrimaryDisplay() is { } d && Current(d) is { } dm ? dm.dmDisplayFrequency : null;

    /// <summary>Frequencies available at the current resolution and depth.</summary>
    public static int[] AvailableRefreshRates()
    {
        if (PrimaryDisplay() is not { } d || Current(d) is not { } cur) return [];
        var set = new SortedSet<int>();
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        for (var i = 0; EnumDisplaySettings(d, i, ref dm); i++)
        {
            if (dm.dmPelsWidth == cur.dmPelsWidth && dm.dmPelsHeight == cur.dmPelsHeight && dm.dmBitsPerPel == cur.dmBitsPerPel) set.Add(dm.dmDisplayFrequency);
            dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        }
        return set.ToArray();
    }

    /// <summary>Same resolution and depth, only the frequency. Returns the frequency read back, or null if the change was refused.</summary>
    public static int? SetRefreshHz(int hz)
    {
        if (PrimaryDisplay() is not { } d || Current(d) is not { } dm) return null;
        if (dm.dmDisplayFrequency == hz) return hz;
        if (!AvailableRefreshRates().Contains(hz)) return null;
        dm.dmDisplayFrequency = hz;
        dm.dmFields = DM_DISPLAYFREQUENCY;
        var r = ChangeDisplaySettingsEx(d, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        return r == DISP_CHANGE_SUCCESSFUL ? RefreshHz : null;
    }

    // ---- brightness ---------------------------------------------------

    public static int? Brightness
    {
        get
        {
            try
            {
                using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return null;
        }
    }

    public static int? SetBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject o in s.Get())
            {
                var p = o.GetMethodParameters("WmiSetBrightness");
                p["Timeout"] = 1u;
                p["Brightness"] = (byte)percent;
                o.InvokeMethod("WmiSetBrightness", p, null);
                Thread.Sleep(300);
                return Brightness;
            }
        }
        catch { }
        return null;
    }

    // ---- power scheme (powercfg) ---------------------------------------

    public const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    public const string ProcThrottleMax = "bc5038f7-23e0-4960-96da-33abaf5935ec";   // max processor state, %
    public const string PerfBoostMode = "be337238-0d82-4146-a960-4f3749d470c7";     // 0 disabled .. 6
    public const string SubPciExpress = "501a4d13-42af-4429-9fd1-a8218c268e20";
    public const string Aspm = "ee12f906-d277-404b-b6da-e5fa1a576df5";              // 0 off, 1 moderate, 2 maximum
    public const string SubWireless = "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1";
    public const string WifiPowerSave = "12bbebe6-58d6-4636-95bb-3217ef867c1a";     // 0 max performance .. 3 max power saving
    public const string SubUsb = "2a737441-1930-4402-8d77-b2bebba308a3";
    public const string UsbSelectiveSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226"; // 0 disabled, 1 enabled

    /// <summary>The DC settings the battery profile touches: (subgroup, setting, label, battery value).</summary>
    public static readonly (string Sub, string Setting, string Label, int Battery)[] DcSettings =
    [
        (SubProcessor, PerfBoostMode, "boost mode", 0),
        (SubProcessor, ProcThrottleMax, "max processor state %", 99),
        (SubPciExpress, Aspm, "PCIe ASPM", 2),
        (SubWireless, WifiPowerSave, "Wi-Fi power saving", 3),
        (SubUsb, UsbSelectiveSuspend, "USB selective suspend", 1),
    ];

    /// <summary>What an index means for one of the settings above, for people (the raw number says nothing).</summary>
    public static string DcName(string setting, int value) => setting switch
    {
        PerfBoostMode => value switch
        {
            0 => "disabled", 1 => "enabled", 2 => "aggressive", 3 => "efficient enabled", 4 => "efficient aggressive",
            5 => "aggressive at guaranteed", 6 => "efficient aggressive at guaranteed", _ => value.ToString(),
        },
        ProcThrottleMax => $"{value} %",
        Aspm => value switch { 0 => "off", 1 => "moderate", 2 => "maximum", _ => value.ToString() },
        WifiPowerSave => value switch { 0 => "max performance", 1 => "low saving", 2 => "medium saving", 3 => "max saving", _ => value.ToString() },
        UsbSelectiveSuspend => value switch { 0 => "disabled", 1 => "enabled", _ => value.ToString() },
        _ => value.ToString(),
    };

    /// <summary>Current (AC, DC) indices of a setting in the active scheme, or null if powercfg does not know it.</summary>
    public static (int Ac, int Dc)? Query(string sub, string setting)
    {
        var text = Powercfg($"/query SCHEME_CURRENT {sub} {setting}");
        if (text is null) return null;
        // Localised labels; the two hex indices at the end are "AC" then "DC" in every language.
        var hex = Regex.Matches(text, @"0x[0-9A-Fa-f]{8}").Select(m => Convert.ToInt32(m.Value, 16)).ToList();
        if (hex.Count < 2) return null;
        return (hex[^2], hex[^1]);
    }

    /// <summary>Writes the DC index and re-activates the scheme (required for the value to take effect). Returns the DC value read back.</summary>
    public static int? SetDc(string sub, string setting, int value)
    {
        if (Powercfg($"/setdcvalueindex SCHEME_CURRENT {sub} {setting} {value}") is null) return null;
        Powercfg("/setactive SCHEME_CURRENT");
        return Query(sub, setting)?.Dc;
    }

    private static string? Powercfg(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg.exe", args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? output : null;
        }
        catch { return null; }
    }

    // ---- power-mode slider (overlay), read only -------------------------

    public const string OverlayBestEfficiency = "961cc777-2547-4f9d-8174-7d86181b8a7a";
    public const string OverlayBestPerformance = "ded574b5-45a0-4f42-8737-46345c09c238";

    /// <summary>The slider position Windows applies on AC and on battery (it switches between them itself).</summary>
    public static (string Ac, string Dc) Overlays()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
            return (OverlayName(k?.GetValue("ActiveOverlayAcPowerScheme") as string), OverlayName(k?.GetValue("ActiveOverlayDcPowerScheme") as string));
        }
        catch { return ("?", "?"); }
    }

    private static string OverlayName(string? guid) => guid?.ToLowerInvariant() switch
    {
        OverlayBestEfficiency => "best power efficiency",
        OverlayBestPerformance => "best performance",
        null or "" or "00000000-0000-0000-0000-000000000000" => "balanced",
        var g => g,
    };
}
