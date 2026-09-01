using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab dev toast [--title t] [--body b]
/// Sends a sample notification through the exact path the guard uses.
/// </summary>
public static class ToastCommand
{
    public static int Run(Args args)
    {
        var title = args.Get("title") ?? "rycolab: test notification";
        var body = args.Get("body") ?? $"sent {DateTime.Now:HH:mm:ss} from `rycolab dev toast`";
        if (Notifier.Notify(title, body)) { Console.WriteLine("  Toast sent (chime plays if the wav is next to the exe)."); return 0; }
        Console.Error.WriteLine("  The toast could not be shown (WinRT refused or notifications are blocked).");
        return 1;
    }
}
