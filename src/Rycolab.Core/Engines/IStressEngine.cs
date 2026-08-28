namespace Rycolab.Core.Engines;

public enum EngineState { Running, Clean, Error, Crashed }

/// <summary>
/// Clean is only produced by Stop() after the time ran out with no error; a
/// process that ends on its own, whatever the exit code, is Crashed
/// (y-cruncher with StopOnError exits 1 after a compute error: Error wins
/// over Crashed).
/// </summary>
public sealed record EngineStatus(EngineState State, string? Error, int? ExitCode, int Lines, string? LastLine, int Suspensions);

public interface IStressEngine : IDisposable
{
    string Name { get; }
    string OutputPath { get; }
    void Start(int core, string workDir);
    EngineStatus Poll();
    /// <summary>Kills the process if still alive and returns the final verdict.</summary>
    EngineStatus Stop();
}
