using System.Text.Json;

namespace Rycolab.Core;

/// <summary>Cumulative validation of the current profile, kept across guard runs.</summary>
public sealed class Validation
{
    public DateTime StartedAt { get; set; }
    public string ProfileKey { get; set; } = "";
    public long GuardedSeconds { get; set; }
    public int Whea { get; set; }
    public int Resumes { get; set; }
    public int Reapplies { get; set; }
    /// <summary>Unexpected reboots (Kernel-Power 41) between one guard tick and the next guard start. A reset without WHEA is still a reset.</summary>
    public int Resets { get; set; }
    /// <summary>Last tick written by the previous guard; the reset check on start looks from here.</summary>
    public DateTime? LastTickAt { get; set; }

    public const int SteadyAfterHours = 20;
    public const int SteadyAfterDays = 7;
    /// <summary>The calendar route still needs the guard to have actually watched something.</summary>
    public const int SteadyMinGuardedHours = 8;

    public bool IsSteady => IsSteadyAt(DateTime.Now);

    public bool IsSteadyAt(DateTime now)
        => Whea == 0 && Resets == 0
           && (GuardedSeconds >= SteadyAfterHours * 3600L
               || ((now - StartedAt).TotalDays >= SteadyAfterDays && GuardedSeconds >= SteadyMinGuardedHours * 3600L));

    public static string KeyOf(Profile p) => string.Join(",", p.Cores);

    public static Validation LoadFor(Profile p)
    {
        var v = Journal.ReadJsonFile<Validation>(AppPaths.Validation);
        if (v is null || v.ProfileKey != KeyOf(p)) v = new Validation { StartedAt = DateTime.Now, ProfileKey = KeyOf(p) };
        return v;
    }

    public void Save() => Journal.WriteJsonFile(AppPaths.Validation, this);
}

/// <summary>
/// state.json: what the guard is doing, written atomically on every sample
/// and event so `status` and the bare `rycolab` command can read it without
/// elevation.
/// </summary>
public sealed class State
{
    public string Phase { get; set; } = "off";          // off | validating | steady | positive
    /// <summary>Why the phase is `positive`: "whea" (hardware error) or "lost" (the margins kept being overwritten and the guard gave up).</summary>
    public string? Positive { get; set; }
    public int? GuardPid { get; set; }
    public DateTime? Since { get; set; }
    public int[]? Profile { get; set; }
    public int?[]? Hardware { get; set; }
    public bool Applied { get; set; }
    public DateTime? LastTick { get; set; }
    public string? LastState { get; set; }
    public int Whea { get; set; }
    public double? CpuLoad { get; set; }
    public double? PackagePower { get; set; }
    public long GuardedSeconds { get; set; }
    public int Resumes { get; set; }
    public int Reapplies { get; set; }
    public int Resets { get; set; }
    /// <summary>"battery" while the guard's power auto has the battery profile applied; "ac" otherwise; null when power auto is off.</summary>
    public string? PowerProfile { get; set; }
    public DateTime? ValidationStartedAt { get; set; }
    public List<string> LastEvents { get; set; } = [];
    public string? LastError { get; set; }

    public static State? Load()
    {
        try
        {
            if (!File.Exists(AppPaths.State)) return null;
            using var fs = new FileStream(AppPaths.State, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return JsonSerializer.Deserialize<State>(fs);
        }
        catch { return null; }
    }

    public void Save() => Journal.WriteJsonFile(AppPaths.State, this);
}
