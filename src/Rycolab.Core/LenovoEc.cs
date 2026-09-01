using System.Management;

namespace Rycolab.Core;

/// <summary>
/// Fan speeds and EC temperatures on Lenovo Legion machines, through the
/// same WMI method Legion Toolkit uses (LENOVO_OTHER_METHOD.GetFeatureValue).
/// HWiNFO and LibreHardwareMonitor do not see these fans. Optional: on a
/// machine without the class every read is null.
/// </summary>
public sealed class LenovoEc : IDisposable
{
    private readonly ManagementObject? _obj;

    private const uint CpuFan = 0x04030001, GpuFan = 0x04030002, PchFan = 0x04030004;
    private const uint CpuTemp = 0x05040000, GpuTemp = 0x05050000, PchTemp = 0x05010000;
    private const uint FanFullSpeedId = 0x04020000;

    public bool IsAvailable => _obj is not null;

    public LenovoEc()
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_OTHER_METHOD");
            _obj = s.Get().Cast<ManagementObject>().FirstOrDefault();
        }
        catch { _obj = null; }
    }

    public int? CpuFanRpm => Read(CpuFan);
    public int? GpuFanRpm => Read(GpuFan);
    public int? PchFanRpm => Read(PchFan);
    public int? CpuTempC => Read(CpuTemp);
    public int? GpuTempC => Read(GpuTemp);
    public int? PchTempC => Read(PchTemp);

    /// <summary>
    /// The "fan full speed" switch (Legion Toolkit's "Maximum fan speed"). It
    /// drives the fans past the top level of the EC's own table (5700 vs 5200
    /// RPM on the reference machine) and, unlike the table, ramps in seconds.
    /// </summary>
    public bool? FanFullSpeed => Read(FanFullSpeedId) is { } v ? v != 0 : null;

    /// <summary>Same WMI call Legion Toolkit makes. Returns false when the EC refused or is absent.</summary>
    public bool SetFanFullSpeed(bool on)
    {
        if (_obj is null) return false;
        try
        {
            var p = _obj.GetMethodParameters("SetFeatureValue");
            p["IDs"] = FanFullSpeedId;
            p["value"] = on ? 1 : 0;
            using var r = _obj.InvokeMethod("SetFeatureValue", p, null);
            return true;
        }
        catch { return false; }
    }

    private int? Read(uint id)
    {
        if (_obj is null) return null;
        try
        {
            var p = _obj.GetMethodParameters("GetFeatureValue");
            p["IDs"] = id;
            using var r = _obj.InvokeMethod("GetFeatureValue", p, null);
            return Convert.ToInt32(r["Value"]);
        }
        catch { return null; }
    }

    /// <summary>
    /// The EC's power mode as LENOVO_GAMEZONE_DATA.GetSmartFanMode reports it:
    /// 1 quiet, 2 balanced, 3 performance, 224 extreme, 255 custom (Legion
    /// Toolkit's "custom mode"). Null when the class is absent.
    /// </summary>
    public int? SmartFanMode => GameZone("GetSmartFanMode");

    /// <summary>LENOVO_GAMEZONE_DATA call, as Legion Toolkit makes it. Null when the class is absent or the EC refused.</summary>
    private static int? GameZone(string method, (string Name, object Value)? param = null)
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_GAMEZONE_DATA");
            using var g = s.Get().Cast<ManagementObject>().FirstOrDefault();
            if (g is null) return null;
            ManagementBaseObject? p = null;
            if (param is { } pr) { p = g.GetMethodParameters(method); p[pr.Name] = pr.Value; }
            using var r = g.InvokeMethod(method, p, null);
            return r?.Properties.Cast<PropertyData>().Any(x => x.Name == "Data") == true ? Convert.ToInt32(r["Data"]) : 0;
        }
        catch { return null; }
    }

    // ---- iGPU mode (Legion Toolkit's "Hybrid mode": On / On-iGPU only / On-Auto) ----

    public const int IGpuDefault = 0, IGpuOnly = 1, IGpuAuto = 2;

    /// <summary>GetIGPUModeStatus: 0 default (hybrid), 1 iGPU only (dGPU off), 2 auto (iGPU only on battery, hybrid on AC; the firmware switches).</summary>
    public int? IGpuMode => GameZone("GetIGPUModeStatus");

    /// <summary>SetIGPUModeStatus. No reboot on Legion (Toolkit only touches the BIOS on ThinkBook). Returns the mode read back after 1 s.</summary>
    public int? SetIGpuMode(int mode)
    {
        if (GameZone("SetIGPUModeStatus", ("mode", mode)) is null) return null;
        Thread.Sleep(1000);
        return IGpuMode;
    }

    /// <summary>NotifyDGPUStatus: what Toolkit sends after a mode change, with whether the dGPU PnP node is still present.</summary>
    public bool NotifyDgpuStatus(bool present) => GameZone("NotifyDGPUStatus", ("Status", present ? 1 : 0)) is not null;

    /// <summary>
    /// The NVIDIA display adapter is enumerated and healthy (same question
    /// Toolkit answers with SetupDi). PnP data only: Win32_VideoController
    /// goes through the display driver and wakes the card, which kept it
    /// from ever idling into the pending ejection (2026-09-01, the guard
    /// and the status viewer polling it every minute / every 2 s).
    /// </summary>
    public static bool DgpuPresent()
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\CIMV2", @"SELECT ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPClass = 'Display' AND PNPDeviceID LIKE 'PCI\\VEN_10DE%'");
            return s.Get().Cast<ManagementObject>().Any(o => Convert.ToInt32(o["ConfigManagerErrorCode"]) == 0);
        }
        catch { return false; }
    }

    public static string IGpuModeName(int? mode) => mode switch { 0 => "hybrid", 1 => "igpu-only", 2 => "auto", null => "?", _ => mode.ToString()! };

    /// <summary>
    /// LENOVO_GAMEZONE_DATA.SetSmartFanMode, what Legion Toolkit calls to change
    /// the power mode. The custom slot (255) runs with the limits last written
    /// into it (read them with <see cref="PowerLimits"/>); rycolab never writes
    /// limits. Returns the mode read back, or null if the EC refused.
    /// </summary>
    public int? SetSmartFanMode(int mode)
    {
        if (GameZone("SetSmartFanMode", ("Data", mode)) is null) return null;
        Thread.Sleep(500);
        return SmartFanMode;
    }

    /// <summary>CPU power limits in effect (W) and the CPU temperature limit (C), as Legion Toolkit reads them. Null entries: not readable.</summary>
    public (int? Pl1, int? Pl2, int? Peak, int? Cross, int? TempLimit) PowerLimits
        => (Read(0x01020000), Read(0x01010000), Read(0x01030000), Read(0x01060000), Read(0x01040000));

    public static string Describe((int? Pl1, int? Pl2, int? Peak, int? Cross, int? TempLimit) l)
        => $"PL1 {l.Pl1?.ToString() ?? "?"} W, PL2 {l.Pl2?.ToString() ?? "?"} W, peak {l.Peak?.ToString() ?? "?"} W, cross {l.Cross?.ToString() ?? "?"} W, CPU limit {l.TempLimit?.ToString() ?? "?"} C";

    // WMI value = Legion Toolkit's PowerModeState + 1 (AbstractWmiFeature offset 1):
    // quiet 1, balanced 2, performance 3, extreme 224, custom (god mode) 255.
    // Verified 2026-08-30: SetSmartFanMode(0) is invalid and silently ignored by the EC.
    public const int QuietMode = 1;
    public const int CustomMode = 255;

    public static string ModeName(int? mode) => mode switch
    {
        1 => "quiet", 2 => "balanced", 3 => "performance", 224 => "extreme", 255 => "custom", null => "?", _ => mode.ToString()!
    };

    public void Dispose() => _obj?.Dispose();
}
