using System.Runtime.InteropServices;

namespace Rycolab.Core;

/// <summary>
/// The guard's loop says which stage it enters; a stage that has not moved
/// on within the limit is a stall, reported once, and so is its end. Times
/// are QueryUnbiasedInterruptTime (100 ns, sleep and hibernation left out),
/// so a night asleep in the wait is not a stall.
/// </summary>
internal sealed class StallWatch(TimeSpan limit)
{
    private readonly object _lock = new();
    private string _stage = "start";
    private long _since = Now();
    private string? _stuckStage;
    private long _stuckSince;

    public void Enter(string stage, long? now = null)
    {
        lock (_lock) { _stage = stage; _since = now ?? Now(); }
    }

    /// <summary>The event to log now, if any: a stall that just passed the limit, or the end of one.</summary>
    public (string Kind, string Detail)? Check(long now)
    {
        lock (_lock)
        {
            if (_stuckStage is null)
            {
                var age = TimeSpan.FromTicks(now - _since);
                if (age < limit) return null;
                _stuckStage = _stage;
                _stuckSince = _since;
                return ("stalled", $"no progress for {age.TotalMinutes:F0} min in '{_stage}': the profile and the GPU curve are not watched while it hangs");
            }
            if (_since == _stuckSince) return null;
            var stuck = TimeSpan.FromTicks(_since - _stuckSince);
            var detail = $"'{_stuckStage}' returned after {stuck.TotalMinutes:F0} min; now in '{_stage}'";
            _stuckStage = null;
            return ("stall-over", detail);
        }
    }

    public static long Now()
    {
        QueryUnbiasedInterruptTime(out var t);
        return t;
    }

    [DllImport("kernel32.dll")]
    private static extern bool QueryUnbiasedInterruptTime(out long time);
}
