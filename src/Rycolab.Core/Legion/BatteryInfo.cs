using System.Management;

namespace Rycolab.Core.Legion;

/// <summary>
/// Battery through the WMI classes the battery class driver publishes
/// (root\WMI BatteryStatus / BatteryFullChargedCapacity): AC line, discharge
/// rate in W, remaining charge. Read fresh on every call; nothing cached.
/// On a machine without a battery every value is null.
/// </summary>
public static class BatteryInfo
{
    public readonly record struct Sample(bool? OnAc, double? DischargeW, double? RemainingWh, double? FullWh, double? ChargeW = null)
    {
        public double? Percent => RemainingWh is { } r && FullWh is > 0 and var f ? Math.Round(100.0 * r / f, 1) : null;
        /// <summary>Hours left at the current discharge rate.</summary>
        public double? HoursLeft => RemainingWh is { } r && DischargeW is > 0 and var w ? r / w : null;
    }

    public static Sample Read()
    {
        bool? onAc = null; double? dischargeW = null, remaining = null, full = null, chargeW = null;
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT PowerOnline, Discharging, DischargeRate, Charging, ChargeRate, RemainingCapacity FROM BatteryStatus");
            foreach (ManagementObject o in s.Get())
            {
                onAc = (bool)o["PowerOnline"];
                var rate = Convert.ToDouble(o["DischargeRate"]) / 1000.0;
                dischargeW = (bool)o["Discharging"] && rate > 0 ? rate : null;
                var charge = Convert.ToDouble(o["ChargeRate"]) / 1000.0;
                chargeW = (bool)o["Charging"] && charge > 0 ? charge : null;
                remaining = Convert.ToDouble(o["RemainingCapacity"]) / 1000.0;
                break;
            }
            using var f = new ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
            foreach (ManagementObject o in f.Get()) { full = Convert.ToDouble(o["FullChargedCapacity"]) / 1000.0; break; }
        }
        catch { /* no battery class: nulls */ }
        return new Sample(onAc, dischargeW, remaining, full, chargeW);
    }

    /// <summary>AC line from the kernel, for the guard's event handler (cheaper than WMI and the same source Windows uses).</summary>
    public static bool? OnAcLine()
    {
        if (!GetSystemPowerStatus(out var sps)) return null;
        return sps.ACLineStatus switch { 0 => false, 1 => true, _ => null };
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
