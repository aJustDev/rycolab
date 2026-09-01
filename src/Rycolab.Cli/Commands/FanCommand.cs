using Rycolab.Core.Legion;
using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab legion fan show | on | off | auto [--on 85] [--off 80] [--hold 3] [--interval 2]
/// Lenovo Legion only: the EC's "fan full speed" switch, by hand or driven
/// by the CPU temperature with hysteresis. The EC's fan table tops out at
/// its level 10 (5200 RPM on the reference machine) and ramps at ~60 RPM/s;
/// the switch goes past it (5700) and ramps in seconds, which measured
/// -3 C and +100 MHz sustained under Cinebench.
///
/// The EC only honours the switch in the custom power mode (255), so `on`
/// and `auto` select it themselves (SetSmartFanMode, what Legion Toolkit
/// does) and remember the previous mode; `off` and the end of `auto` put it
/// back. The custom slot runs with the power limits last written into it;
/// they are printed, never written. `auto` turns the switch on after
/// `--hold` seconds at or above `--on`, off after `--hold` seconds at or
/// below `--off`, and always off on exit (Ctrl+C).
/// </summary>
public static class FanCommand
{
    private static string PrevModeFile => Path.Combine(AppPaths.Data, "fan-prev-mode");

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
                if (!EnterCustomMode(ec)) return 1;
                if (!ec.SetFanFullSpeed(true)) { Console.Error.WriteLine("  The EC refused the write."); return 1; }
                Thread.Sleep(500);
                Show(ec);
                return 0;
            case "off":
                ec.SetFanFullSpeed(false);
                RestoreMode(ec);
                Thread.Sleep(500);
                Show(ec);
                return 0;
            case "auto":
                if (!EnterCustomMode(ec)) return 1;
                return Auto(ec, args.GetInt("on") ?? 85, args.GetInt("off") ?? 80, args.GetInt("hold") ?? 3, args.GetInt("interval") ?? 2);
            default:
                Console.Error.WriteLine("Usage: rycolab legion fan show | on | off | auto [--on 85] [--off 80] [--hold 3] [--interval 2]");
                return 2;
        }
    }

    /// <summary>Switch to the custom mode if needed, remembering where we came from. One mode change, then the limits in effect are printed.</summary>
    private static bool EnterCustomMode(LenovoEc ec)
    {
        var mode = ec.SmartFanMode;
        if (mode == LenovoEc.CustomMode) return true;
        var before = ec.PowerLimits;
        var after = ec.SetSmartFanMode(LenovoEc.CustomMode);
        if (after != LenovoEc.CustomMode)
        {
            Console.Error.WriteLine($"  Could not select the custom power mode (EC reports {LenovoEc.ModeName(after)}).");
            return false;
        }
        if (mode is { } m) { try { File.WriteAllText(PrevModeFile, m.ToString()); } catch { /* best effort */ } }
        var limits = ec.PowerLimits;
        Console.WriteLine($"  power mode {LenovoEc.ModeName(mode)} -> custom (the switch only acts there); limits in effect: {LenovoEc.Describe(limits)}{(limits == before ? " (unchanged)" : $"  [were: {LenovoEc.Describe(before)}]")}");
        return true;
    }

    /// <summary>Back to the mode we found, if we changed it.</summary>
    private static void RestoreMode(LenovoEc ec)
    {
        int prev;
        try
        {
            if (!File.Exists(PrevModeFile) || !int.TryParse(File.ReadAllText(PrevModeFile).Trim(), out prev)) return;
            File.Delete(PrevModeFile);
        }
        catch { return; }
        if (ec.SmartFanMode != LenovoEc.CustomMode) return;   // someone else moved it; leave it alone
        var now = ec.SetSmartFanMode(prev);
        Console.WriteLine($"  power mode custom -> {LenovoEc.ModeName(now)}");
    }

    private static void Show(LenovoEc ec)
    {
        Console.WriteLine();
        Console.WriteLine($"  power mode   {LenovoEc.ModeName(ec.SmartFanMode)}   {LenovoEc.Describe(ec.PowerLimits)}");
        Console.WriteLine($"  full speed   {(ec.FanFullSpeed is { } f ? (f ? "ON" : "off") : "?")}{(ec.SmartFanMode == LenovoEc.CustomMode ? "" : "   (ignored by the EC outside the custom mode; `legion fan on` / `legion fan auto` select it)")}");
        Console.WriteLine($"  fans         CPU {ec.CpuFanRpm?.ToString() ?? "-"}   GPU {ec.GpuFanRpm?.ToString() ?? "-"}   PCH {ec.PchFanRpm?.ToString() ?? "-"} RPM");
        Console.WriteLine($"  EC temps     CPU {ec.CpuTempC?.ToString() ?? "-"}   GPU {ec.GpuTempC?.ToString() ?? "-"}   PCH {ec.PchTempC?.ToString() ?? "-"} C");
        Console.WriteLine();
    }

    private static int Auto(LenovoEc ec, int onAt, int offAt, int hold, int interval)
    {
        if (offAt >= onAt) { Console.Error.WriteLine("  --off must be below --on."); return 2; }
        Console.WriteLine();
        Console.WriteLine($"  fan auto: full speed ON after {hold} s at >= {onAt} C, off after {hold} s at <= {offAt} C, EC CPU temperature every {interval} s. Ctrl+C stops, turns it off and restores the power mode.");
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
            Thread.Sleep(1000);
            RestoreMode(ec);
        }
        return 0;
    }

    private static string Elapsed(DateTime t0) => ((int)(DateTime.Now - t0).TotalSeconds).ToString().PadLeft(5) + "s";
}
