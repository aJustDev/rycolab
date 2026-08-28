using Microsoft.Win32;

namespace LegionCoLab.Core;

/// <summary>
/// Aviso de suspension y reanudacion via SystemEvents. En consola, .NET monta
/// su propio hilo con bucle de mensajes, asi que basta con suscribirse. La
/// fuente de verdad sigue siendo el margen releido: si este evento no llega,
/// guard lo detecta igual en la siguiente muestra.
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
