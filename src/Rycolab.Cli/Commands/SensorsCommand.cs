using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// Dumps the sensors with their EXACT names on this machine.
/// LibreHardwareMonitor names change between Ryzen generations, so they are
/// mapped by looking at them, not by guessing.
/// </summary>
public static class SensorsCommand
{
    public static int Run(Args args)
    {
        using var telemetry = new Telemetry();

        if (!telemetry.IsAvailable)
        {
            Console.Error.WriteLine($"Telemetry unavailable: {telemetry.Unavailable}");
            return 1;
        }

        Console.WriteLine();
        var lastType = "";
        foreach (var (type, name, value) in telemetry.DumpAll())
        {
            if (type != lastType)
            {
                Console.WriteLine();
                Console.WriteLine($"  == {type} ==");
                lastType = type;
            }
            Console.WriteLine("     {0,-38} {1}", name, value?.ToString("F3") ?? "-");
        }

        Console.WriteLine();
        Console.WriteLine("  How rycolab reads these sensors:");
        var s = telemetry.Read(targetCore: 0);
        Console.WriteLine($"     Core 0 clock   {s.TargetClock?.ToString("F0") ?? "NOT FOUND"} MHz  (effective {s.TargetClockEffective?.ToString("F0") ?? "-"})");
        Console.WriteLine($"     Core 0 VID     {s.TargetVid?.ToString("F3") ?? "NOT FOUND"}");
        Console.WriteLine($"     Core 0 power   {s.TargetPower?.ToString("F3") ?? "NOT FOUND"} W");
        Console.WriteLine($"     Package        {s.PackagePower?.ToString("F1") ?? "NOT FOUND"}");
        Console.WriteLine($"     Tctl           {s.Tctl?.ToString("F1") ?? "NOT FOUND"}");
        Console.WriteLine($"     CCD0 / CCD1    {s.Ccd0Temp?.ToString("F1") ?? "-"} / {s.Ccd1Temp?.ToString("F1") ?? "-"}");
        Console.WriteLine();
        Console.WriteLine("  If anything shows NOT FOUND, fix the patterns in Core/Telemetry.cs.");
        Console.WriteLine();
        return 0;
    }
}
