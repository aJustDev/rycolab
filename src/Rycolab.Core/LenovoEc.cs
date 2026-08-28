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

    public void Dispose() => _obj?.Dispose();
}
