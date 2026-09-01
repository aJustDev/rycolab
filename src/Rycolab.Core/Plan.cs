using Rycolab.Core.Legion;
using System.Text.Json;

namespace Rycolab.Core;

/// <summary>
/// config.json: the baseline the BIOS restores and the parameters of a sweep.
/// The per-core margins to run with live in <see cref="Profile"/>.
/// </summary>
public sealed class Plan
{
    public int Base { get; set; } = -5;
    /// <summary>The sweep engine(s): the widest vector binary the CPU runs, the one that finds the errors. Chosen at `install`; a saved config keeps its own.</summary>
    public string[] Engines { get; set; } = global::Rycolab.Core.Engines.YCruncherBinaries.Recommended();
    public string[] Tests { get; set; } = ["BKT", "BBP", "SFTv4", "SNT", "SVT", "FFTv4", "N63", "VT3"];
    /// <summary>Per run in the sweep and fine stages.</summary>
    public int Seconds { get; set; } = 360;
    public int Step { get; set; } = 5;
    /// <summary>The coarse search step (0 = the fine step, linear search).</summary>
    public int CoarseStep { get; set; } = 10;
    public int Start { get; set; } = -50;
    public int Top { get; set; } = -5;
    public int SafetyMargin { get; set; } = 5;
    /// <summary>The long run at the limit with the sweep engines (0 = skip).</summary>
    public int ConfirmSeconds { get; set; } = 1800;
    /// <summary>Light load at limit + safety margin with <see cref="SoakEngine"/> (0 = skip).</summary>
    public int SoakSeconds { get; set; } = 600;
    /// <summary>The engine that reaches fMax: where an unstable margin shows at light load.</summary>
    public string SoakEngine { get; set; } = global::Rycolab.Core.Engines.YCruncherBinaries.Sse3;

    /// <summary>Every engine a campaign needs on disk.</summary>
    public IEnumerable<string> AllEngines => SoakSeconds > 0 && !string.IsNullOrEmpty(SoakEngine) ? Engines.Append(SoakEngine).Distinct() : Engines;
    public string? YCruncher { get; set; }
    /// <summary>Windows toast + chime when the guard hits bad news (WHEA, reset, margin lost, giveup).</summary>
    public bool Notify { get; set; } = true;
    /// <summary>The guard applies the battery profile when the AC line drops and restores it when it is back (`rycolab legion power auto`).</summary>
    public bool PowerAuto { get; set; }
    public PowerOptions PowerAutoOptions { get; set; } = new();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string DefaultPath => AppPaths.Config;

    /// <summary>The installed config, or the defaults if there is none.</summary>
    public static Plan LoadOrDefault(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return new Plan();
        return Load(path);
    }

    public static Plan Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) throw new FileNotFoundException($"config not found: {path}");
        var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(path), Json)
                   ?? throw new InvalidDataException($"empty config: {path}");
        plan.Validate();
        return plan;
    }

    public void Save(string? path = null)
    {
        Validate();
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }

    public void Validate()
    {
        Safety.ValidateMargin(Base, "baseline");
        // The absolute range, not the CPU's floor: a config written before the floor was known must still load
        // (install raises Start; a write below the floor is refused at the SMU side anyway).
        if (Start < Safety.AbsoluteMinMargin || Start > Safety.AbsoluteMaxMargin)
            throw new SafetyViolationException($"start {Start} is outside {Safety.AbsoluteMinMargin}..{Safety.AbsoluteMaxMargin}.");
        Safety.ValidateMargin(Top, "top");
        if (Step <= 0) throw new SafetyViolationException("step must be positive.");
        if (Start > Top) throw new SafetyViolationException($"start {Start} is above top {Top}.");
        if (Seconds <= 0) throw new SafetyViolationException("seconds must be positive.");
        if (CoarseStep < 0 || (CoarseStep > 0 && CoarseStep % Step != 0)) throw new SafetyViolationException($"coarseStep must be 0 or a multiple of step ({Step}).");
        if (ConfirmSeconds < 0 || SoakSeconds < 0) throw new SafetyViolationException("confirmSeconds and soakSeconds cannot be negative.");
        if (SafetyMargin < 0) throw new SafetyViolationException("safetyMargin cannot be negative.");
        if (Engines.Length == 0 || Tests.Length == 0) throw new SafetyViolationException("at least one engine and one test are required.");
    }

    public string YCruncherDir => YCruncher is { } y ? Environment.ExpandEnvironmentVariables(y) : AppPaths.YCruncher;
}
