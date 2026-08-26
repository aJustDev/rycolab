namespace LegionCoLab.Core;

/// <summary>
/// Red de seguridad para cualquier bloque que escriba margenes.
///
/// Captura el estado ANTES de tocar nada. Si el proceso muere sin llamar a
/// <see cref="Commit"/> — Ctrl+C, excepcion, cierre de consola — devuelve los
/// nucleos a como estaban.
///
/// El peligro que cubre no es el valor final sino el estado a medias: quedarse
/// con ocho nucleos escritos y ocho sin escribir es peor que cualquiera de las
/// dos configuraciones enteras.
///
/// La base no se codifica a mano: es lo que hubiera puesto al empezar. Si
/// arrancaste desde el -5 de la BIOS, ahi vuelve.
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

        _onCancel = (_, e) => { e.Cancel = false; Rollback("interrumpido con Ctrl+C"); };
        _onExit = (_, _) => Rollback("el proceso termino sin confirmar");

        Console.CancelKeyPress += _onCancel;
        AppDomain.CurrentDomain.ProcessExit += _onExit;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
    }

    private void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        => Rollback("excepcion no controlada");

    /// <summary>La configuracion actual es la que se quiere dejar puesta.</summary>
    public void Commit() => _committed = true;

    private void Rollback(string reason)
    {
        if (_committed || _disposed) return;
        _disposed = true;

        try
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"  !! {reason} — restaurando el estado anterior");

            var restored = 0;
            foreach (var (core, margin) in _before)
            {
                try
                {
                    _co.WriteCoreUnchecked(core, margin);
                    restored++;
                }
                catch { /* en panico se sigue con el siguiente */ }
            }

            Console.Error.WriteLine($"  !! restaurados {restored} de {_before.Count} nucleos");
            Console.Error.WriteLine("  !! si algo quedo raro, un reinicio devuelve el equipo a los valores de la BIOS");
        }
        catch { /* no queda nada mejor que hacer */ }
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= _onCancel;
        AppDomain.CurrentDomain.ProcessExit -= _onExit;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;

        if (!_committed) Rollback("bloque abandonado sin confirmar");
        _disposed = true;
    }
}
