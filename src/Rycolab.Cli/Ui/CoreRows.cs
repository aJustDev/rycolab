using Rycolab.Core;

namespace Rycolab.Cli.Ui;

/// <summary>One text line per CCD present: "CCD0  0:x  1:y ..." and "CCD1 ..." only if there are more than 8 cores.</summary>
public static class CoreRows
{
    public static IEnumerable<string> Lines(int coreCount, Func<int, string> cell, string separator = "  ")
    {
        var count = Math.Clamp(coreCount, 1, Topology.MaxCores);
        for (var first = 0; first < count; first += Topology.CoresPerCcd)
        {
            var last = Math.Min(count, first + Topology.CoresPerCcd);
            yield return $"{Topology.CcdNameFromIndex(first / Topology.CoresPerCcd)}{separator}{string.Join(separator, Enumerable.Range(first, last - first).Select(cell))}";
        }
    }

    /// <summary>Core count implied by a set of core indices, rounded up to whole CCDs.</summary>
    public static int CountFor(IEnumerable<int> cores)
    {
        var max = cores.DefaultIfEmpty(0).Max();
        return Math.Min(Topology.MaxCores, (max / Topology.CoresPerCcd + 1) * Topology.CoresPerCcd);
    }
}
