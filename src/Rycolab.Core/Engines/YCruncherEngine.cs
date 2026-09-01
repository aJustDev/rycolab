using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Rycolab.Core.Engines;

/// <summary>
/// y-cruncher pinned to one physical core (both logicals), with the config
/// CoreCycler generates (script-corecycler.ps1:8568-8603) and its command
/// line (1237, 8421: pause:-2 colors:0 priority:-1 config cfg). stdin is
/// redirected: without that it sits waiting for a key press.
///
/// Signals, validated on 2026-08-27 (docs/lab-notebook.md, phase 1):
///   error   a line containing error/fail/mismatch/invalid/exception/crash,
///           minus "0 errors", "no errors", "passed" and the "Stop on Error" header
///   crash   the process ends on its own (exit != 0, e.g. 0xc0000005)
/// </summary>
public sealed partial class YCruncherEngine : IStressEngine
{
    private readonly string _exe;
    private readonly string[] _tests;
    private readonly long _memoryBytes;
    private readonly bool _suspend;

    private Process? _p;
    private StreamWriter? _out;
    private readonly List<string> _lines = [];
    private readonly object _gate = new();
    private string? _error;
    private int _suspensions;
    private Thread? _suspender;
    private volatile bool _stopping;

    public string Name { get; }
    public string OutputPath { get; private set; } = "";

    public YCruncherEngine(string binariesDir, string mode, string[] tests, long memoryBytes = 1L << 30, bool suspend = true)
    {
        Name = mode;
        _exe = Path.Combine(binariesDir, mode + ".exe");
        if (!File.Exists(_exe)) throw new FileNotFoundException($"not found: {_exe}");
        _tests = tests;
        _memoryBytes = memoryBytes;
        _suspend = suspend;
    }

    public void Start(int core, string workDir)
    {
        Directory.CreateDirectory(workDir);
        var cfg = Path.Combine(workDir, "stressTest.cfg");
        var tests = string.Join("\n", _tests.Select(t => $"            \"{t}\""));
        File.WriteAllText(cfg, $$"""
            {
                Action : "StressTest"
                StressTest : {
                    AllocateLocally : "true"
                    LogicalCores : [{{Topology.LogicalProcessor(core)}}]
                    TotalMemory : {{_memoryBytes}}
                    SecondsPerTest : 60
                    SecondsTotal : 0
                    StopOnError : "true"
                    Tests : [
            {{tests}}
                    ]
                }
            }
            """, new UTF8Encoding(false));

        OutputPath = Path.Combine(workDir, "output.txt");
        _out = new StreamWriter(OutputPath, append: false) { AutoFlush = true };

        var psi = new ProcessStartInfo(_exe, $"pause:-2 colors:0 priority:-1 config \"{cfg}\"")
        {
            WorkingDirectory = Path.GetDirectoryName(_exe),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        _p = Process.Start(psi) ?? throw new InvalidOperationException("y-cruncher did not start");
        _p.OutputDataReceived += (_, e) => OnLine(e.Data);
        _p.ErrorDataReceived += (_, e) => OnLine(e.Data);
        _p.BeginOutputReadLine();
        _p.BeginErrorReadLine();

        Thread.Sleep(900);
        try { _p.ProcessorAffinity = (IntPtr)(3L << Topology.LogicalProcessor(core)); }
        catch (InvalidOperationException) { /* already dead: Poll will say so */ }

        if (_suspend)
        {
            _suspender = new Thread(SuspendLoop) { IsBackground = true, Name = "ycr-suspend" };
            _suspender.Start();
        }
    }

    private void OnLine(string? line)
    {
        if (line is null) return;
        lock (_gate)
        {
            _lines.Add(line);
            _out?.WriteLine(line);
            if (_error is null && IsErrorLine(line))
                _error = line.Trim();
        }
    }

    /// <summary>The compute-error criterion, on one output line.</summary>
    internal static bool IsErrorLine(string line) => ErrorPattern().IsMatch(line) && !BenignPattern().IsMatch(line);

    private void SuspendLoop()
    {
        while (!_stopping && _p is { HasExited: false })
        {
            for (var i = 0; i < 90 && !_stopping; i++) Thread.Sleep(100);
            if (_stopping || _p.HasExited) break;
            try
            {
                ThreadControl.Suspend(_p.Id);
                Thread.Sleep(1000);
                ThreadControl.Resume(_p.Id);
                _suspensions++;
            }
            catch { /* the process died in between: a crash is the positive, not our failure */ }
        }
    }

    public EngineStatus Poll()
    {
        lock (_gate)
        {
            var last = _lines.Count > 0 ? _lines[^1] : null;
            if (_error is not null) return new(EngineState.Error, _error, ExitCodeOrNull(), _lines.Count, last, _suspensions);
            if (_p is { HasExited: true }) return new(EngineState.Crashed, null, _p.ExitCode, _lines.Count, last, _suspensions);
            return new(EngineState.Running, null, null, _lines.Count, last, _suspensions);
        }
    }

    public EngineStatus Stop()
    {
        _stopping = true;
        if (_p is { HasExited: false })
        {
            try { _p.Kill(true); _p.WaitForExit(5000); } catch { /* already gone */ }
            // Killed on purpose: no error and no self-exit is a clean run
            Thread.Sleep(200);
            lock (_gate)
            {
                var last = _lines.Count > 0 ? _lines[^1] : null;
                if (_error is not null) return new(EngineState.Error, _error, null, _lines.Count, last, _suspensions);
                return new(EngineState.Clean, null, null, _lines.Count, last, _suspensions);
            }
        }
        return Poll();
    }

    private int? ExitCodeOrNull() => _p is { HasExited: true } ? _p.ExitCode : null;

    public void Dispose()
    {
        _stopping = true;
        try { if (_p is { HasExited: false }) _p.Kill(true); } catch { }
        _p?.Dispose();
        _out?.Dispose();
    }

    [GeneratedRegex(@"error|fail|mismatch|invalid|exception|crash", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorPattern();

    [GeneratedRegex(@"0 errors|no errors|passed|Stop on Error", RegexOptions.IgnoreCase)]
    private static partial Regex BenignPattern();
}
