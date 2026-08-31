using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab charge [show] | normal | conservation | rapid | night on|off
/// Lenovo battery charge modes through the Energy driver (what Legion
/// Toolkit's battery section does): conservation stops at ~80 % (firmware
/// threshold, protects the pack when living on AC), rapid charges fastest,
/// normal is the default. Night charge is a separate slow-overnight toggle.
/// </summary>
public static class ChargeCommand
{
    public static int Run(Args args)
    {
        var sub = args.Positional.FirstOrDefault()?.ToLowerInvariant() ?? "show";
        using var energy = new LenovoEnergy();
        if (!energy.IsAvailable) { Console.Error.WriteLine(@"  No Lenovo Energy driver here (\\.\EnergyDrv): charge modes need a Lenovo machine with it."); return 1; }

        switch (sub)
        {
            case "show":
                Show(energy);
                return 0;
            case LenovoEnergy.Normal:
            case LenovoEnergy.Conservation:
            case LenovoEnergy.Rapid:
            {
                var before = energy.ChargeMode();
                var after = energy.SetChargeMode(sub);
                Console.WriteLine($"  charge mode {before ?? "?"} -> {after ?? "?"}{(after == sub ? "" : "  (the driver did not confirm the change)")}");
                Show(energy);
                return after == sub ? 0 : 1;
            }
            case "night":
            {
                var want = args.Positional.Skip(1).FirstOrDefault()?.ToLowerInvariant();
                if (want is not ("on" or "off")) { Console.Error.WriteLine("Usage: rycolab charge night on|off"); return 2; }
                if (energy.NightCharge() is null) { Console.Error.WriteLine("  Night charge is not supported on this machine."); return 1; }
                var after = energy.SetNightCharge(want == "on");
                Console.WriteLine($"  night charge -> {(after is { } a ? (a ? "on" : "off") : "?")}{(after == (want == "on") ? "" : "  (the driver did not confirm the change)")}");
                return after == (want == "on") ? 0 : 1;
            }
            default:
                Console.Error.WriteLine("Usage: rycolab charge [show] | normal | conservation | rapid | night on|off");
                return 2;
        }
    }

    private static void Show(LenovoEnergy energy)
    {
        var b = BatteryInfo.Read();
        var night = energy.NightCharge();
        Console.WriteLine();
        Console.WriteLine($"  charge mode  {energy.ChargeMode() ?? "?"}   (normal | conservation: stops at ~80 % | rapid)");
        Console.WriteLine($"  night charge {(night is { } n ? (n ? "on" : "off") : "not supported")}");
        Console.WriteLine($"  battery      {(b.Percent is { } p ? $"{p:F0} %" : "?")}  {(b.RemainingWh is { } r ? $"{r:F1} / {b.FullWh:F1} Wh" : "")}  {(b.OnAc == true ? "on AC" : "discharging")}");
        Console.WriteLine();
    }
}
