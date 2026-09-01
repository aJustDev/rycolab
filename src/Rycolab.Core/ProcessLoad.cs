using System.Diagnostics;

namespace Rycolab.Core;

/// <summary>
/// Who is burning the CPU right now: per-process CPU time sampled between
/// calls (the live status panel calls every 2 s, which is the sampling
/// window). Unelevated it sees the user's own processes; system ones deny
/// the query and are skipped.
/// </summary>
public static class ProcessLoad
{
    public sealed record Entry(string Name, int Pid, double CpuPct, double BusyShare);

    private static Dictionary<int, (string Name, double Cpu)> _prev = [];
    private static DateTime _prevAt;

    /// <summary>First call primes and returns empty; later calls return the top consumers since the previous one.</summary>
    public static List<Entry> Top(int n, double minPct = 0.5)
    {
        var now = DateTime.Now;
        var cur = new Dictionary<int, (string, double)>();
        foreach (var p in Process.GetProcesses())
        {
            try { cur[p.Id] = (p.ProcessName, p.TotalProcessorTime.TotalSeconds); }
            catch { /* exited, or a system process this token cannot query */ }
            finally { p.Dispose(); }
        }

        var wall = (now - _prevAt).TotalSeconds;
        var result = new List<Entry>();
        if (_prev.Count > 0 && wall > 0.4)
        {
            var deltas = new List<(string Name, int Pid, double Sec)>();
            foreach (var (pid, (name, cpu)) in cur)
                if (_prev.TryGetValue(pid, out var b) && b.Name == name && cpu > b.Cpu)
                    deltas.Add((name, pid, cpu - b.Cpu));
            var busy = deltas.Sum(d => d.Sec);
            result = deltas
                .Select(d => new Entry(d.Name, d.Pid, d.Sec / wall / Environment.ProcessorCount * 100, busy > 0 ? d.Sec / busy : 0))
                .Where(e => e.CpuPct >= minPct)
                .OrderByDescending(e => e.CpuPct)
                .Take(n)
                .ToList();
        }

        _prev = cur;
        _prevAt = now;
        return result;
    }
}
