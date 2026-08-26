using LegionCoLab.Core;

namespace LegionCoLab.Cli.Commands;

/// <summary>
/// Vuelca los sensores con su nombre EXACTO en esta maquina. Los nombres de
/// LibreHardwareMonitor cambian entre generaciones de Ryzen, asi que se mapean
/// mirandolos, no adivinandolos.
/// </summary>
public static class SensorsCommand
{
    public static int Run(Args args)
    {
        using var telemetry = new Telemetry();

        if (!telemetry.IsAvailable)
        {
            Console.Error.WriteLine($"Telemetria no disponible: {telemetry.Unavailable}");
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
            Console.WriteLine("     {0,-38} {1}", name, value?.ToString("F3") ?? "—");
        }

        Console.WriteLine();
        Console.WriteLine("  Interpretacion que hace colab de estos sensores:");
        var s = telemetry.Read(targetCore: 0);
        Console.WriteLine($"     Reloj nuc.0  {s.TargetClock?.ToString("F0") ?? "NO ENCONTRADO"} MHz  (efectivo {s.TargetClockEffective?.ToString("F0") ?? "—"})");
        Console.WriteLine($"     VID nuc.0    {s.TargetVid?.ToString("F3") ?? "NO ENCONTRADO"}");
        Console.WriteLine($"     Pot. nuc.0   {s.TargetPower?.ToString("F3") ?? "NO ENCONTRADO"} W");
        Console.WriteLine($"     Paquete      {s.PackagePower?.ToString("F1") ?? "NO ENCONTRADO"}");
        Console.WriteLine($"     Tctl         {s.Tctl?.ToString("F1") ?? "NO ENCONTRADO"}");
        Console.WriteLine($"     CCD1 / CCD2  {s.Ccd1Temp?.ToString("F1") ?? "—"} / {s.Ccd2Temp?.ToString("F1") ?? "—"}");
        Console.WriteLine();
        Console.WriteLine("  Si algo sale NO ENCONTRADO, hay que corregir los patrones en Core/Telemetry.cs.");
        Console.WriteLine();
        return 0;
    }
}
