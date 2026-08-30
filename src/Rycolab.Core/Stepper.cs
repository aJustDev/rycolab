namespace Rycolab.Core;

/// <summary>
/// Applies a set of margins walking in stops of at most
/// <see cref="Safety.MaxStepBetweenLevels"/> counts, reading back at every
/// stop, under <see cref="SafetySession"/>. The only write path used by
/// apply, guard and sweep.
/// </summary>
public static class Stepper
{
    /// <summary>Path from <paramref name="from"/> to <paramref name="to"/> with no step above the limit.</summary>
    public static int[] BuildPath(int from, int to)
    {
        if (from == to) return [];
        var path = new List<int>();
        var cur = from;
        while (cur != to)
        {
            var remaining = to - cur;
            cur += Math.Sign(remaining) * Math.Min(Math.Abs(remaining), Safety.MaxStepBetweenLevels);
            path.Add(cur);
        }
        return [.. path];
    }

    /// <summary>
    /// Writes the targets and returns the final read. Throws
    /// <see cref="CoWriteFailedException"/> if any core did not land where requested.
    /// </summary>
    public static IReadOnlyList<CoreReading> Apply(CoController co, IReadOnlyList<(int Core, int Margin)> targets, Action<string>? log = null)
    {
        foreach (var (core, margin) in targets)
            Safety.ValidateMargin(margin, $"core {core}: margin");
        var current = co.ReadAll().Where(r => r.Margin.HasValue).ToDictionary(r => r.Index, r => r.Margin!.Value);
        var plans = targets.Where(t => current.ContainsKey(t.Core))
                           .Select(t => (t.Core, Path: BuildPath(current[t.Core], t.Margin)))
                           .Where(p => p.Path.Length > 0)
                           .ToList();

        if (plans.Count > 0)
        {
            using var session = new SafetySession(co);
            var maxLen = plans.Max(p => p.Path.Length);
            for (var stop = 0; stop < maxLen; stop++)
            {
                foreach (var (core, path) in plans)
                    if (stop < path.Length) co.WriteCore(core, path[stop]);
                log?.Invoke($"stop {stop + 1}/{maxLen} verified");
            }
            session.Commit();
        }

        var after = co.ReadAll();
        var bad = targets.Where(t => current.ContainsKey(t.Core) && after[t.Core].Margin != t.Margin).Select(t => t.Core).ToList();
        if (bad.Count > 0)
            throw new CoWriteFailedException($"cores that did not reach their target: {string.Join(", ", bad)}");
        return after;
    }
}
