using Rycolab.Core.Legion;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab legion mode [quiet|balanced|performance|extreme|custom]
/// Lenovo Legion only: the EC's power mode, shown or set through
/// SetSmartFanMode (what Legion Toolkit calls) and read back. Fn+Q on the
/// reference machine cycles quiet, balanced and performance only: extreme
/// and custom are reachable only from software, and once the mode fell to
/// quiet (2026-09-20) nothing short of Legion Toolkit brought extreme back.
/// </summary>
public static class ModeCommand
{
    public static int Run(Args args)
    {
        var name = args.Positional.FirstOrDefault();
        using var ec = new LenovoEc();
        if (!ec.IsAvailable) { Console.Error.WriteLine("  No Lenovo EC here (LENOVO_OTHER_METHOD not found): the power mode only exists on Legion machines."); return 1; }

        var before = ec.SmartFanMode;
        var limitsBefore = ec.PowerLimits;
        if (name is null or "show")
        {
            Console.WriteLine($"  power mode {LenovoEc.ModeName(before)}   limits in effect: {LenovoEc.Describe(limitsBefore)}");
            return 0;
        }
        if (LenovoEc.ModeFromName(name) is not { } mode)
        {
            Console.Error.WriteLine("Usage: rycolab legion mode [quiet|balanced|performance|extreme|custom]");
            return 2;
        }

        var after = ec.SetSmartFanMode(mode);
        var limits = ec.PowerLimits;
        // The limits readout trails a mode change (2026-09-16: extreme's limits still read right after switching to quiet).
        Console.WriteLine($"  power mode {LenovoEc.ModeName(before)} -> {LenovoEc.ModeName(after)}{(after == mode ? "" : " (FAILED)")}; limits in effect: {LenovoEc.Describe(limits)}{(limits == limitsBefore && after != before ? " (unchanged so far: the EC can take a few seconds; `rycolab legion mode` reads them again)" : "")}");
        return after == mode ? 0 : 1;
    }
}
