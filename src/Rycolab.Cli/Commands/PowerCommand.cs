using Rycolab.Core.Legion;
using System.Diagnostics;
using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab legion power show | battery [--gpu igpu|auto|keep] [--mode quiet|keep] [--hz 60] [--brightness 40] [--close-apps] [--no-windows]
///               | ac | restore | auto on|off
/// Lenovo Legion battery profile: EC quiet mode, iGPU only, 60 Hz, dimmer
/// panel, DC power-scheme values; `ac` puts everything back from the
/// snapshot taken before the first change. `auto on` makes the guard apply
/// battery/ac itself when the AC line changes (15 s debounce).
/// </summary>
public static class PowerCommand
{
    public static int Run(Args args)
    {
        var sub = args.Positional.FirstOrDefault()?.ToLowerInvariant() ?? "show";
        using var ec = new LenovoEc();
        if (!ec.IsAvailable && sub != "show") { Console.Error.WriteLine("  No Lenovo EC here (LENOVO_OTHER_METHOD not found): the profile needs a Legion machine."); return 1; }

        switch (sub)
        {
            case "show":
                Show(ec);
                return 0;
            case "battery":
            {
                var o = new PowerOptions
                {
                    Gpu = args.Get("gpu")?.ToLowerInvariant() ?? "igpu",
                    Mode = args.Get("mode")?.ToLowerInvariant() ?? "quiet",
                    Hz = args.Has("hz") ? args.GetInt("hz") : 60,
                    Brightness = args.Has("brightness") ? args.GetInt("brightness") : 40,
                    Windows = !args.Has("no-windows"),
                    CloseApps = args.Has("close-apps"),
                };
                if (o.Gpu is not ("igpu" or "auto" or "keep")) { Console.Error.WriteLine("  --gpu must be igpu, auto or keep."); return 2; }
                if (o.Mode is not ("quiet" or "keep")) { Console.Error.WriteLine("  --mode must be quiet or keep."); return 2; }
                Console.WriteLine();
                Console.WriteLine($"  battery profile: {o}");
                var failed = PowerProfile.Battery(ec, o, Line);
                Show(ec);
                if (failed > 0) Console.Error.WriteLine($"  {failed} knob(s) failed; the rest is applied. `legion power ac` restores the snapshot.");
                return failed > 0 ? 1 : 0;
            }
            case "ac":
            case "restore":
            {
                Console.WriteLine();
                var failed = PowerProfile.Ac(ec, Line, force: sub == "restore");
                Show(ec);
                return failed > 0 ? 1 : 0;
            }
            case "auto":
            {
                var on = args.Positional.Skip(1).FirstOrDefault()?.ToLowerInvariant();
                if (on is not ("on" or "off")) { Console.Error.WriteLine("Usage: rycolab legion power auto on|off [--gpu ...] [--hz ...] [--brightness ...] [--no-windows] [--close-apps]"); return 2; }
                var plan = Rycolab.Core.Plan.LoadOrDefault();
                plan.PowerAuto = on == "on";
                if (on == "on")
                    plan.PowerAutoOptions = new PowerOptions
                    {
                        Gpu = args.Get("gpu")?.ToLowerInvariant() ?? "igpu",
                        Mode = args.Get("mode")?.ToLowerInvariant() ?? "quiet",
                        Hz = args.Has("hz") ? args.GetInt("hz") : 60,
                        Brightness = args.Has("brightness") ? args.GetInt("brightness") : 40,
                        Windows = !args.Has("no-windows"),
                        CloseApps = args.Has("close-apps"),
                    };
                plan.Save();
                Console.WriteLine($"  power auto {on}{(on == "on" ? $": the guard applies the battery profile 15 s after the AC line drops ({plan.PowerAutoOptions}) and restores it 15 s after it is back" : "")}.");
                Console.WriteLine(Service.GuardProcess() is null ? "  The guard is not running (`rycolab on`); the setting applies when it starts." : "  The running guard picks it up on its next loop.");
                return 0;
            }
            default:
                Console.Error.WriteLine("Usage: rycolab legion power show | battery [--gpu igpu|auto|keep] [--mode quiet|keep] [--hz 60] [--brightness 40] [--close-apps] [--no-windows] | ac | restore | auto on|off");
                return 2;
        }
    }

    private static void Line(string s) => Console.WriteLine($"  {DateTime.Now:HH:mm:ss}  {s}");

    private static void Show(LenovoEc ec)
    {
        var b = BatteryInfo.Read();
        var plan = Rycolab.Core.Plan.LoadOrDefault();
        var apps = PowerProfile.BackgroundApps.Where(n => Process.GetProcessesByName(n).Length > 0).ToList();
        var (oac, odc) = WindowsPower.Overlays();
        Console.WriteLine();
        Console.WriteLine($"  line         {(b.OnAc is { } ac ? (ac ? "AC" : "battery") : "?")}   {(b.DischargeW is { } w ? $"{w:F1} W" : "-")}   {(b.Percent is { } p ? $"{p:F0} %" : "-")}   {(b.RemainingWh is { } r ? $"{r:F1} / {b.FullWh:F1} Wh" : "-")}{(b.HoursLeft is { } h ? $"   ~{h:F1} h at this rate" : "")}");
        if (ec.IsAvailable)
        {
            Console.WriteLine($"  power mode   {LenovoEc.ModeName(ec.SmartFanMode)}   {LenovoEc.Describe(ec.PowerLimits)}");
            Console.WriteLine($"  gpu          {LenovoEc.IGpuModeName(ec.IGpuMode)}   dGPU {(LenovoEc.DgpuPresent() ? "present" : "off")}");
            Console.WriteLine($"  fans         CPU {ec.CpuFanRpm?.ToString() ?? "-"}   GPU {ec.GpuFanRpm?.ToString() ?? "-"}   PCH {ec.PchFanRpm?.ToString() ?? "-"} RPM   EC CPU {ec.CpuTempC?.ToString() ?? "-"} C");
        }
        Console.WriteLine($"  panel        {WindowsPower.RefreshHz?.ToString() ?? "?"} Hz (available {string.Join(",", WindowsPower.AvailableRefreshRates())})   brightness {WindowsPower.Brightness?.ToString() ?? "?"} %");
        Console.WriteLine($"  windows      slider AC {oac} / battery {odc}");
        foreach (var (sub, setting, label, _) in WindowsPower.DcSettings)
            if (WindowsPower.Query(sub, setting) is { } q) Console.WriteLine($"               {label,-24} AC {q.Ac}   DC {q.Dc}");
        Console.WriteLine($"  apps         {(apps.Count > 0 ? string.Join(", ", apps) + " running" : "none of " + string.Join(", ", PowerProfile.BackgroundApps))}");
        Console.WriteLine($"  snapshot     {(PowerSnapshot.Load() is { } s ? $"battery profile applied at {s.TakenAt:HH:mm:ss} (`legion power ac` restores)" : "none (nothing applied)")}");
        Console.WriteLine($"  auto         {(plan.PowerAuto ? $"on: {plan.PowerAutoOptions}" : "off")}");
        Console.WriteLine();
    }
}
