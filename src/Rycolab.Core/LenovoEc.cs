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

    public const int CustomMode = 255;

    public static string ModeName(int? mode) => mode switch
    {
        0 => "quiet", 1 => "balanced", 2 => "performance", 224 => "extreme", 255 => "custom", null => "?", _ => mode.ToString()!
    };

    public void Dispose() => _obj?.Dispose();
}
