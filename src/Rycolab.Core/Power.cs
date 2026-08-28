using Microsoft.Win32;

namespace Rycolab.Core;

/// <summary>
/// Sleep and resume notifications via SystemEvents. In a console app .NET
/// spins its own message-loop thread, so subscribing is enough. The source
/// of truth is still the margin read back: if this event never arrives,
/// guard catches it on the next sample anyway.
/// </summary>
public sealed class PowerWatch : IDisposable
{
    private readonly PowerModeChangedEventHandler _handler;

    public event Action? Resumed;
    public event Action? Suspending;

    public PowerWatch()
    {
        _handler = (_, e) =>
        {
            if (e.Mode == PowerModes.Resume) Resumed?.Invoke();
            else if (e.Mode == PowerModes.Suspend) Suspending?.Invoke();
        };
        SystemEvents.PowerModeChanged += _handler;
    }

    public void Dispose() => SystemEvents.PowerModeChanged -= _handler;
}
