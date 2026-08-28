using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab fan show | on | off | auto [--on 90] [--off 80] [--hold 6] [--interval 2]
/// Lenovo Legion only: the EC's "fan full speed" switch, by hand or driven
/// by the CPU temperature with hysteresis. The EC's fan table tops out at
/// its level 10 (5200 RPM on the reference machine) and ramps at ~60 RPM/s;
/// the switch goes past it (5700) and ramps in seconds, which measured
/// -3 C and +100 MHz sustained under Cinebench. `auto` turns it on after
/// `--hold` seconds at or above `--on`, off after `--hold` seconds at or
/// below `--off`, and always off on exit (Ctrl+C).
/// </summary>
public static class FanCommand
{
    public static int Run(Args args)
    {
        var sub = args.Positional.FirstOrDefault()?.ToLowerInvariant() ?? "show";
        using var ec = new LenovoEc();
        if (!ec.IsAvailable) { Console.Error.WriteLine("  No Lenovo EC here (LENOVO_OTHER_METHOD not found): the fan switch only exists on Legion machines."); return 1; }

        switch (sub)
        {
            case "show":
                Show(ec);
                return 0;
            case "on":
            case "off":
                if (sub == "on" && !CustomMode(ec)) return 2;
                if (!ec.SetFanFullSpeed(sub == "on")) { Console.Error.WriteLine("  The EC refused the write."); return 1; }
                Thread.Sleep(500);
                Show(ec);
                return 0;
            case "auto":
                if (!CustomMode(ec)) return 2;
                return Auto(ec, args.GetInt("on") ?? 90, args.GetInt("off") ?? 80, args.GetInt("hold") ?? 6, args.GetInt("interval") ?? 2);
            default:
                Console.Error.WriteLine("Usage: rycolab fan show | on | off | auto [--on 90] [--off 80] [--hold 6] [--interval 2]");
                return 2;
        }
    }

    private static bool CustomMode(LenovoEc ec)
    {
        if (ec.SmartFanMode == LenovoEc.CustomMode) return true;
        Console.Error.WriteLine($"  Power mode is {LenovoEc.ModeName(ec.SmartFanMode)}: the EC ignores the full speed switch outside Legion Toolkit custom mode. Select the custom mode there first.");
        return false;
    }

    private static void Show(LenovoEc ec)
    {
        Console.WriteLine();
        Console.WriteLine($"  power mode   {LenovoEc.ModeName(ec.SmartFanMode)}{(ec.SmartFanMode == LenovoEc.CustomMode ? "" : "   (the full speed switch only acts in Legion Toolkit custom mode)")}");
        Console.WriteLine($"  full speed   {(ec.FanFullSpeed is { } f ? (f ? "ON" : "off") : "?")}");
        Console.WriteLine($"  fans         CPU {ec.CpuFanRpm?.ToString() ?? "-"}   GPU {ec.GpuFanRpm?.ToString() ?? "-"}   PCH {ec.PchFanRpm?.ToString() ?? "-"} RPM");
        Console.WriteLine($"  EC temps     CPU {ec.CpuTempC?.ToString() ?? "-"}   GPU {ec.GpuTempC?.ToString() ?? "-"}   PCH {ec.PchTempC?.ToString() ?? "-"} C");
        Console.WriteLine();
    }

    private static int Auto(LenovoEc ec, int onAt, int offAt, int hold, int interval)
    {
        if (offAt >= onAt) { Console.Error.WriteLine("  --off must be below --on."); return 2; }
        Console.WriteLine();
        Console.WriteLine($"  fan auto: full speed ON after {hold} s at >= {onAt} C, off after {hold} s at <= {offAt} C, EC CPU temperature every {interval} s. Ctrl+C stops and turns it off.");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var on = ec.FanFullSpeed ?? false;
        var since = DateTime.Now;
        var t0 = DateTime.Now;
        int? pending = null;   // 1 = counting towards ON, 0 = towards off
        var lastMode = ec.SmartFanMode;
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var mode = ec.SmartFanMode;
                if (mode != lastMode)
                {
                    Console.WriteLine($"  {Elapsed(t0)}  power mode changed to {LenovoEc.ModeName(mode)}{(mode == LenovoEc.CustomMode ? "" : ": the EC ignores the switch until custom mode is back (Legion Toolkit changes it on AC events, resume and start)")}");
                    lastMode = mode;
                }
                var t = ec.CpuTempC;
                if (t is { } temp)
                {
                    var want = on ? (temp <= offAt ? 0 : (int?)null) : (temp >= onAt ? 1 : (int?)null);
                    if (want is null) pending = null;
                    else if (pending != want) { pending = want; since = DateTime.Now; }
                    else if ((DateTime.Now - since).TotalSeconds >= hold)
                    {
                        on = want == 1;
                        pending = null;
                        var ok = ec.SetFanFullSpeed(on);
                        Console.WriteLine($"  {Elapsed(t0)}  {temp} C  -> full speed {(on ? "ON" : "off")}{(ok ? "" : " (EC refused)")}");
                    }
                }
                cts.Token.WaitHandle.WaitOne(interval * 1000);
            }
        }
        finally
        {
            ec.SetFanFullSpeed(false);
            Console.WriteLine($"  {Elapsed(t0)}  stopped, full speed off");
        }
        return 0;
    }

    private static string Elapsed(DateTime t0) => ((int)(DateTime.Now - t0).TotalSeconds).ToString().PadLeft(5) + "s";
}
