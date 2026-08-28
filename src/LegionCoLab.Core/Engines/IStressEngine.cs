namespace LegionCoLab.Core.Engines;

public enum EngineState { Running, Clean, Error, Crashed }

/// <summary>
/// Clean solo lo da Stop() tras agotar el tiempo sin error; un proceso que
/// termina solo, con el codigo que sea, es Crashed (y-cruncher con
/// StopOnError sale con 1 tras un error de calculo: Error gana a Crashed).
/// </summary>
public sealed record EngineStatus(EngineState State, string? Error, int? ExitCode, int Lines, string? LastLine, int Suspensions);

public interface IStressEngine : IDisposable
{
    string Name { get; }
    string OutputPath { get; }
    void Start(int core, string workDir);
    EngineStatus Poll();
    /// <summary>Mata el proceso si sigue vivo y devuelve el veredicto final.</summary>
    EngineStatus Stop();
}
