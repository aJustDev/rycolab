using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// Back to the baseline on every core: the value the BIOS leaves at POST
/// (config.json, read by `install`), i.e. what a reboot returns to.
/// </summary>
public static class ResetCommand
{
    public static int Run(Args args)
    {
        var to = args.GetInt("to") ?? Plan.LoadOrDefault().Base;

        Console.WriteLine();
        Console.WriteLine($"  Returning all cores to {to}.");

        var forwarded = new List<string> { "--margin", to.ToString() };
        if (args.Has("dry-run")) forwarded.Add("--dry-run");

        return ApplyCommand.Run(new Args(forwarded));
    }
}
