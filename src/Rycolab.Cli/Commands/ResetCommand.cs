namespace Rycolab.Cli.Commands;

/// <summary>
/// Back to the baseline on every core.
///
/// The default baseline is -5: what the all-core BIOS setting (Sign -,
/// Magnitude 5) leaves applied at POST on the reference machine, i.e. what
/// the machine returns to just by rebooting.
/// </summary>
public static class ResetCommand
{
    public const int DefaultBaseline = -5;

    public static int Run(Args args)
    {
        var to = args.GetInt("to") ?? DefaultBaseline;

        Console.WriteLine();
        Console.WriteLine($"  Returning all cores to {to}.");

        var forwarded = new List<string> { "--margin", to.ToString() };
        if (args.Has("dry-run")) forwarded.Add("--dry-run");

        return ApplyCommand.Run(new Args(forwarded));
    }
}
