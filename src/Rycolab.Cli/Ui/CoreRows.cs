using Rycolab.Core;

namespace Rycolab.Cli.Ui;

/// <summary>One text line per CCD present in the first <paramref name="coreCount"/> cores of the map: "CCD0  0:x  1:y ..." then "CCD1 ...".</summary>
public static class CoreRows
{
    public static IEnumerable<string> Lines(int coreCount, Func<int, string> cell, string separator = "  ")
    {
        var count = Math.Clamp(coreCount, 1, Topology.MaxCores);
        foreach (var g in Enumerable.Range(0, count).GroupBy(Topology.CcdOf).OrderBy(g => g.Key))
            yield return $"{Topology.CcdNameFromIndex(g.Key)}{separator}{string.Join(separator, g.Select(cell))}";
    }

    /// <summary>Core count implied by a set of core indices, rounded up to whole CCDs of the map.</summary>
    public static int CountFor(IEnumerable<int> cores)
    {
        var max = cores.DefaultIfEmpty(0).Max();
        var lastCcd = Topology.CcdOf(max);
        var whole = Enumerable.Range(0, Topology.MaxCores).Count(i => Topology.CcdOf(i) <= lastCcd);
        return Math.Min(Topology.MaxCores, Math.Max(whole, max + 1));
    }
}
