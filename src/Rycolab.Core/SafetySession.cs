namespace Rycolab.Core;

/// <summary>
/// Safety net around any block that writes margins.
///
/// Captures the state BEFORE touching anything. If the process dies without
/// calling <see cref="Commit"/> (Ctrl+C, exception, console closed) the cores
/// go back to what they were.
///
/// The danger it covers is not the final value but the half-written state:
/// eight cores written and eight not is worse than either full configuration.
///
/// The baseline is not hard-coded: it is whatever was there at the start. If
/// you started from the BIOS value, that is where you return.
/// </summary>
public sealed class SafetySession : IDisposable
{
    private readonly CoController _co;
    private readonly Dictionary<int, int> _before = [];
    private readonly ConsoleCancelEventHandler _onCancel;
    private readonly EventHandler _onExit;

    private bool _committed;
    private bool _disposed;

    public IReadOnlyDictionary<int, int> Before => _before;

    public SafetySession(CoController co)
    {
        _co = co;

        foreach (var r in co.ReadAll())
            if (r.Margin is { } m)
                _before[r.Index] = m;

        _onCancel = (_, e) => { e.Cancel = false; Rollback("interrupted with Ctrl+C"); };
        _onExit = (_, _) => Rollback("the process ended without committing");

        Console.CancelKeyPress += _onCancel;
        AppDomain.CurrentDomain.ProcessExit += _onExit;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
    }

    private void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        => Rollback("unhandled exception");

    /// <summary>The current configuration is the one we want to keep.</summary>
    public void Commit() => _committed = true;

    private void Rollback(string reason)
    {
        if (_committed || _disposed) return;
        _disposed = true;

        try
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"  !! {reason} - restoring the previous state");

            var restored = 0;
            foreach (var (core, margin) in _before)
            {
                try
                {
                    _co.WriteCoreUnchecked(core, margin);
                    restored++;
                }
                catch { /* in panic mode, keep going with the next core */ }
            }

            Console.Error.WriteLine($"  !! restored {restored} of {_before.Count} cores");
            Console.Error.WriteLine("  !! if anything looks off, a reboot returns the machine to the BIOS values");
        }
        catch { /* nothing better left to do */ }
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= _onCancel;
        AppDomain.CurrentDomain.ProcessExit -= _onExit;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;

        if (!_committed) Rollback("block left without committing");
        _disposed = true;
    }
}
