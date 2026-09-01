using System.Diagnostics;
using System.Management;
using System.Xml;

namespace Rycolab.Core;

public sealed record HealthSample(DateTime Ts, double? FullWh, double? DesignWh, int? Cycles);

/// <summary>
/// The pack's real capacity over time: FullChargedCapacity and CycleCount
/// from root\WMI, design capacity from one `powercfg /batteryreport` XML
/// (BatteryStaticData fails on the reference machine even elevated) cached
/// in battery-design.json - it never changes for a given pack.
/// </summary>
public static class BatteryHealth
{
    public static HealthSample Read()
    {
        double? full = null; int? cycles = null;
        try
        {
            using var f = new ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
            foreach (ManagementObject o in f.Get()) { full = Convert.ToDouble(o["FullChargedCapacity"]) / 1000.0; break; }
            using var c = new ManagementObjectSearcher(@"root\WMI", "SELECT CycleCount FROM BatteryCycleCount");
            foreach (ManagementObject o in c.Get()) { cycles = Convert.ToInt32(o["CycleCount"]); break; }
        }
        catch { /* no battery class: nulls */ }
        return new HealthSample(DateTime.Now, full, DesignWh(), cycles);
    }

    private sealed record Design(double DesignWh);

    public static double? DesignWh()
    {
        if (Journal.ReadJsonFile<Design>(AppPaths.BatteryDesign) is { } cached) return cached.DesignWh;
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"rycolab-batteryreport-{Environment.ProcessId}.xml");
            var p = Process.Start(new ProcessStartInfo("powercfg.exe", $"/batteryreport /output \"{tmp}\" /xml")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
            p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            if (!File.Exists(tmp)) return null;
            var xml = new XmlDocument();
            xml.Load(tmp);
            File.Delete(tmp);
            var node = xml.GetElementsByTagName("DesignCapacity").Cast<XmlNode>().FirstOrDefault();
            if (node is null || !double.TryParse(node.InnerText, out var mwh) || mwh <= 0) return null;
            var design = new Design(mwh / 1000.0);
            Journal.WriteJsonFile(AppPaths.BatteryDesign, design);
            return design.DesignWh;
        }
        catch { return null; }
    }
}
