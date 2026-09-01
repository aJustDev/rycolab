namespace Rycolab.Core;

/// <summary>
/// Applies a set of margins under <see cref="SafetySession"/>: one write per
/// core, each read back, and a final read of everything. The only write
/// path used by apply, guard and sweep. (Until 0.3.0 a move was walked in
/// stops of 3 counts; the SMU applies a margin atomically, so the stops
/// only cost time. The read-back is the safety.)
/// </summary>
public static class Stepper
{
    /// <summary>
    /// Writes the targets and returns the final read. Throws
    /// <see cref="CoWriteFailedException"/> if any core did not land where requested.
    /// </summary>
    public static IReadOnlyList<CoreReading> Apply(CoController co, IReadOnlyList<(int Core, int Margin)> targets, Action<string>? log = null)
    {
        foreach (var (core, margin) in targets)
            Safety.ValidateMargin(margin, $"core {core}: margin");
        var current = co.ReadAll().Where(r => r.Margin.HasValue).ToDictionary(r => r.Index, r => r.Margin!.Value);
        var changes = targets.Where(t => current.TryGetValue(t.Core, out var now) && now != t.Margin).ToList();

        if (changes.Count > 0)
        {
            using var session = new SafetySession(co);
            foreach (var (core, margin) in changes) co.WriteCore(core, margin);
            log?.Invoke($"{changes.Count} cores written and verified");
            session.Commit();
        }

        var after = co.ReadAll();
        var bad = targets.Where(t => current.ContainsKey(t.Core) && after[t.Core].Margin != t.Margin).Select(t => t.Core).ToList();
        if (bad.Count > 0)
            throw new CoWriteFailedException($"cores that did not reach their target: {string.Join(", ", bad)}");
        return after;
    }
}
