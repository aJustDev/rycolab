using System.Text.Json;

namespace Rycolab.Core;

/// <summary>
/// plan.json: the per-core profile to run with and the parameters of the
/// sweep that produces it. One file for guard, sweep and report, validated
/// against Safety before use.
/// </summary>
public sealed class Plan
{
    public int[] Profile { get; set; } = Enumerable.Repeat(-5, Topology.MaxCores).ToArray();
    public int Base { get; set; } = -5;
    public string[] Engines { get; set; } = ["04-P4P", "24-ZN5 ~ Komari"];
    public string[] Tests { get; set; } = ["BKT", "BBP", "SFTv4", "SNT", "SVT", "FFTv4", "N63", "VT3"];
    public int Seconds { get; set; } = 360;
    public int Step { get; set; } = 5;
    public int Start { get; set; } = -50;
    public int Top { get; set; } = -5;
    public int SafetyMargin { get; set; } = 5;
    public string YCruncher { get; set; } = "tools/y-cruncher/Binaries";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Repository root: the first directory upwards that has src/ and docs/.</summary>
    public static string RepoRoot
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d is not null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "src")) && Directory.Exists(Path.Combine(d.FullName, "docs")))
                    return d.FullName;
                d = d.Parent;
            }
            return Directory.GetCurrentDirectory();
        }
    }

    public static string DefaultPath => Path.Combine(RepoRoot, "plan.json");

    public static Plan Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) throw new FileNotFoundException($"plan not found: {path}");
        var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(path), Json)
                   ?? throw new InvalidDataException($"empty plan: {path}");
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
        if (Profile.Length != Topology.MaxCores)
            throw new SafetyViolationException($"the profile has {Profile.Length} values; {Topology.MaxCores} are required.");
        Safety.ValidateMargins(Profile);
        Safety.ValidateMargin(Base, "base");
        Safety.ValidateMargin(Start, "start");
        Safety.ValidateMargin(Top, "top");
        if (Step <= 0) throw new SafetyViolationException("step must be positive.");
        if (Start > Top) throw new SafetyViolationException($"start {Start} is above top {Top}.");
        if (Seconds <= 0) throw new SafetyViolationException("seconds must be positive.");
        if (SafetyMargin < 0) throw new SafetyViolationException("safetyMargin cannot be negative.");
    }

    public string YCruncherDir => Path.IsPathRooted(YCruncher) ? YCruncher : Path.Combine(RepoRoot, YCruncher);

    /// <summary>Cores whose reading does not match the profile.</summary>
    public List<int> Mismatches(IReadOnlyList<CoreReading> readings)
        => readings.Where(r => r.Index < Profile.Length && r.Margin != Profile[r.Index]).Select(r => r.Index).ToList();

    public IReadOnlyList<(int Core, int Margin)> Targets(int coreCount)
        => Enumerable.Range(0, Math.Min(coreCount, Profile.Length)).Select(c => (c, Profile[c])).ToList();
}
