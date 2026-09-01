using Rycolab.Core.Legion;
using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab legion charge [show] | normal | conservation | rapid | full [--target 98] | night on|off
/// Lenovo battery charge modes through the Energy driver (what Legion
/// Toolkit's battery section does): conservation stops at ~80 % (firmware
/// threshold, protects the pack when living on AC), rapid charges fastest,
/// normal is the default. Night charge is a separate slow-overnight toggle.
/// `full` is a one-shot: rapid now, and the guard restores the previous
/// mode when the battery reaches the target.
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
                if (ChargeFull.Pending) { ChargeFull.Delete(); Console.WriteLine("  Pending full charge cancelled."); }
                Show(energy);
                return after == sub ? 0 : 1;
            }
            case "full":
            {
                var target = args.GetInt("target") ?? 98;
                if (target is < 50 or > 100) { Console.Error.WriteLine("  --target: 50-100."); return 2; }
                var b = BatteryInfo.Read();
                if (b.Percent is { } p && p >= target) { Console.WriteLine($"  Battery already at {p:F0} % (target {target} %); nothing to do."); return 0; }
                var before = energy.ChargeMode();
                var restore = before is null or LenovoEnergy.Rapid ? LenovoEnergy.Conservation : before;
                if (energy.SetChargeMode(LenovoEnergy.Rapid) != LenovoEnergy.Rapid) { Console.Error.WriteLine("  The driver did not confirm rapid mode; nothing armed."); return 1; }
                new ChargeFull(target, restore, DateTime.Now).Save();
                Console.WriteLine($"  charge mode {before ?? "?"} -> rapid; back to {restore} at {target} %.");
                Console.WriteLine(Service.GuardProcess() is null
                    ? "  WARNING: the guard is not running, so nothing will restore the mode (start it with `rycolab on`, or set it back by hand)."
                    : "  The guard watches it (one check per minute).");
                if (b.OnAc == false) Console.WriteLine("  Note: on battery right now; it charges when you plug in, the marker waits.");
                Show(energy);
                return 0;
            }
            case "night":
            {
                var want = args.Positional.Skip(1).FirstOrDefault()?.ToLowerInvariant();
                if (want is not ("on" or "off")) { Console.Error.WriteLine("Usage: rycolab legion charge night on|off"); return 2; }
                if (energy.NightCharge() is null) { Console.Error.WriteLine("  Night charge is not supported on this machine."); return 1; }
                var after = energy.SetNightCharge(want == "on");
                Console.WriteLine($"  night charge -> {(after is { } a ? (a ? "on" : "off") : "?")}{(after == (want == "on") ? "" : "  (the driver did not confirm the change)")}");
                return after == (want == "on") ? 0 : 1;
            }
            default:
                Console.Error.WriteLine("Usage: rycolab legion charge [show] | normal | conservation | rapid | full [--target 98] | night on|off");
                return 2;
        }
    }

    private static void Show(LenovoEnergy energy)
    {
        var b = BatteryInfo.Read();
        var night = energy.NightCharge();
        Console.WriteLine();
        Console.WriteLine($"  charge mode  {energy.ChargeMode() ?? "?"}   (normal | conservation: stops at ~80 % | rapid)");
        if (ChargeFull.Load() is { } full) Console.WriteLine($"  full charge  in progress -> back to {full.Restore} at {full.Target} %  (since {full.Started:HH:mm})");
        Console.WriteLine($"  night charge {(night is { } n ? (n ? "on" : "off") : "not supported")}");
        Console.WriteLine($"  battery      {(b.Percent is { } p ? $"{p:F0} %" : "?")}  {(b.RemainingWh is { } r ? $"{r:F1} / {b.FullWh:F1} Wh" : "")}  {(b.OnAc == true ? "on AC" : "discharging")}");
        Console.WriteLine();
    }
}
