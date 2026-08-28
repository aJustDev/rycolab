using System.Text.Json;

namespace Rycolab.Core;

/// <summary>
/// config.json: the baseline the BIOS restores and the parameters of a sweep.
/// The per-core margins to run with live in <see cref="Profile"/>.
/// </summary>
public sealed class Plan
{
    public int Base { get; set; } = -5;
    /// <summary>Default chosen for the CPU that runs `install` (AVX-512 or AVX2 binary); a saved config keeps its own.</summary>
    public string[] Engines { get; set; } = global::Rycolab.Core.Engines.YCruncherBinaries.Recommended();
    public string[] Tests { get; set; } = ["BKT", "BBP", "SFTv4", "SNT", "SVT", "FFTv4", "N63", "VT3"];
    public int Seconds { get; set; } = 360;
    public int Step { get; set; } = 5;
    public int Start { get; set; } = -50;
    public int Top { get; set; } = -5;
    public int SafetyMargin { get; set; } = 5;
    public string? YCruncher { get; set; }

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
        Safety.ValidateMargin(Start, "start");
        Safety.ValidateMargin(Top, "top");
        if (Step <= 0) throw new SafetyViolationException("step must be positive.");
        if (Start > Top) throw new SafetyViolationException($"start {Start} is above top {Top}.");
        if (Seconds <= 0) throw new SafetyViolationException("seconds must be positive.");
        if (SafetyMargin < 0) throw new SafetyViolationException("safetyMargin cannot be negative.");
        if (Engines.Length == 0 || Tests.Length == 0) throw new SafetyViolationException("at least one engine and one test are required.");
    }

    public string YCruncherDir => YCruncher is { } y ? Environment.ExpandEnvironmentVariables(y) : AppPaths.YCruncher;
}
