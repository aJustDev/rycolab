using Microsoft.Win32;

namespace Rycolab.Core;

/// <summary>
/// Sleep, resume and AC-line notifications via SystemEvents. In a console
/// app .NET spins its own message-loop thread, so subscribing is enough.
/// The source of truth is still the margin read back: if this event never
/// arrives, guard catches it on the next sample anyway.
/// </summary>
public sealed class PowerWatch : IDisposable
{
    private readonly PowerModeChangedEventHandler _handler;
    private bool? _onAc;

    public event Action? Resumed;
    public event Action? Suspending;
    /// <summary>Raised with the new line state whenever it differs from the last one seen (StatusChange also fires for battery level).</summary>
    public event Action<bool>? AcLineChanged;

    public PowerWatch()
    {
        _onAc = BatteryInfo.OnAcLine();
        _handler = (_, e) =>
        {
            if (e.Mode == PowerModes.Resume) Resumed?.Invoke();
            else if (e.Mode == PowerModes.Suspend) Suspending?.Invoke();
            else if (e.Mode == PowerModes.StatusChange)
            {
                var now = BatteryInfo.OnAcLine();
                if (now is { } ac && ac != _onAc) { _onAc = ac; AcLineChanged?.Invoke(ac); }
            }
        };
        SystemEvents.PowerModeChanged += _handler;
    }

    public void Dispose() => SystemEvents.PowerModeChanged -= _handler;
}
