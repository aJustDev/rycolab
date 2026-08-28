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
    /// 0 quiet, 1 balanced, 2 performance, 224 extreme, 255 custom (Legion
    /// Toolkit's "custom mode"). Null when the class is absent.
    /// </summary>
    public int? SmartFanMode
    {
        get
        {
            try
            {
                using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_GAMEZONE_DATA");
                using var g = s.Get().Cast<ManagementObject>().FirstOrDefault();
                if (g is null) return null;
                using var r = g.InvokeMethod("GetSmartFanMode", null, null);
                return Convert.ToInt32(r["Data"]);
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// LENOVO_GAMEZONE_DATA.SetSmartFanMode, what Legion Toolkit calls to change
    /// the power mode. The custom slot (255) runs with the limits last written
    /// into it (read them with <see cref="PowerLimits"/>); rycolab never writes
    /// limits. Returns the mode read back, or null if the EC refused.
    /// </summary>
    public int? SetSmartFanMode(int mode)
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_GAMEZONE_DATA");
            using var g = s.Get().Cast<ManagementObject>().FirstOrDefault();
            if (g is null) return null;
            var p = g.GetMethodParameters("SetSmartFanMode");
            p["Data"] = mode;
            using var r = g.InvokeMethod("SetSmartFanMode", p, null);
            Thread.Sleep(500);
            return SmartFanMode;
        }
        catch { return null; }
    }

    /// <summary>CPU power limits in effect (W) and the CPU temperature limit (C), as Legion Toolkit reads them. Null entries: not readable.</summary>
    public (int? Pl1, int? Pl2, int? Peak, int? Cross, int? TempLimit) PowerLimits
        => (Read(0x01020000), Read(0x01010000), Read(0x01030000), Read(0x01060000), Read(0x01040000));

    public static string Describe((int? Pl1, int? Pl2, int? Peak, int? Cross, int? TempLimit) l)
        => $"PL1 {l.Pl1?.ToString() ?? "?"} W, PL2 {l.Pl2?.ToString() ?? "?"} W, peak {l.Peak?.ToString() ?? "?"} W, cross {l.Cross?.ToString() ?? "?"} W, CPU limit {l.TempLimit?.ToString() ?? "?"} C";

    public const int CustomMode = 255;

    public static string ModeName(int? mode) => mode switch
    {
        0 => "quiet", 1 => "balanced", 2 => "performance", 224 => "extreme", 255 => "custom", null => "?", _ => mode.ToString()!
    };

    public void Dispose() => _obj?.Dispose();
}
