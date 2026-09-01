using System.Text.Json;

namespace Rycolab.Core;

/// <summary>Where a profile came from. A profile without a source is not applied by `on`.</summary>
public sealed class ProfileSource
{
    public string Campaign { get; set; } = "";
    public DateTime Date { get; set; }
    public int?[] Limits { get; set; } = new int?[Topology.MaxCores];
    public int SafetyMargin { get; set; }
    public string[] Engines { get; set; } = [];
    public string[] Tests { get; set; } = [];
    public int Seconds { get; set; }
    /// <summary>The confirmation and soak stages the limits went through (0: none, as before 0.3.0).</summary>
    public int ConfirmSeconds { get; set; }
    public int SoakSeconds { get; set; }
    public string? SoakEngine { get; set; }
    public string? Note { get; set; }
}

/// <summary>The CPU a profile was measured on. A profile from another CPU is never applied.</summary>
public sealed class CpuFingerprint
{
    public string CpuName { get; set; } = "";
    public int Cores { get; set; }
    public string SmuType { get; set; } = "";

    public static CpuFingerprint Of(CoController co) => new() { CpuName = co.CpuName, Cores = co.CoreCount, SmuType = co.SmuType };

    public bool Matches(CpuFingerprint other)
        => string.Equals(CpuName, other.CpuName, StringComparison.OrdinalIgnoreCase) && Cores == other.Cores && SmuType == other.SmuType;
}

/// <summary>
/// profile.json: the per-core margins to run with, the baseline the BIOS
/// restores, where the numbers came from and which CPU they belong to.
/// </summary>
public sealed class Profile
{
    public int[] Cores { get; set; } = Enumerable.Repeat(-5, Topology.MaxCores).ToArray();
    public int Base { get; set; } = -5;
    public ProfileSource? Source { get; set; }
    public CpuFingerprint? Fingerprint { get; set; }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static bool Exists(string? path = null) => File.Exists(path ?? AppPaths.Profile);

    public static Profile Load(string? path = null)
    {
        path ??= AppPaths.Profile;
        if (!File.Exists(path)) throw new FileNotFoundException($"profile not found: {path}");
        var p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException($"empty profile: {path}");
        p.Validate();
        return p;
    }

    public void Save(string? path = null)
    {
        Validate();
        path ??= AppPaths.Profile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }

    public void Validate()
    {
        if (Cores.Length != Topology.MaxCores)
            throw new SafetyViolationException($"the profile has {Cores.Length} values; {Topology.MaxCores} are required.");
        Safety.ValidateMargins(Cores);
        Safety.ValidateMargin(Base, "baseline");
    }

    /// <summary>
    /// Why this profile must not be applied, or null if it may. Enforced by
    /// `on`; `dev apply --force` is the only way around it.
    /// </summary>
    public string? RefusalReason(CoController co) => RefusalReason(CpuFingerprint.Of(co));

    public string? RefusalReason(CpuFingerprint here)
    {
        if (Source is null) return "the profile has no source (it was not produced by a sweep). Use 'rycolab find', or import it with a source.";
        if (Fingerprint is null) return "the profile has no CPU fingerprint.";
        if (!Fingerprint.Matches(here))
            return $"the profile belongs to another CPU ({Fingerprint.CpuName}, {Fingerprint.Cores} cores); this one is {here.CpuName}, {here.Cores} cores.";
        for (var c = 0; c < Cores.Length; c++)
        {
            var limit = c < Source.Limits.Length ? Source.Limits[c] : null;
            if (limit is { } l && Cores[c] < l)
                return $"core {c} is set to {Cores[c]}, more aggressive than its measured limit {l}.";
        }
        return null;
    }

    public List<int> Mismatches(IReadOnlyList<CoreReading> readings)
        => readings.Where(r => r.Index < Cores.Length && r.Margin != Cores[r.Index]).Select(r => r.Index).ToList();

    public IReadOnlyList<(int Core, int Margin)> Targets(int coreCount)
        => Enumerable.Range(0, Math.Min(coreCount, Cores.Length)).Select(c => (c, Cores[c])).ToList();

    /// <summary>Profile = limit + margin (capped at top); cores without a limit stay at the baseline.</summary>
    public static Profile FromLimits(IReadOnlyDictionary<int, int?> limits, Plan config, string campaign, CpuFingerprint fingerprint, int? margin = null)
    {
        var m = margin ?? config.SafetyMargin;
        var p = new Profile
        {
            Base = config.Base,
            Fingerprint = fingerprint,
            Source = new ProfileSource
            {
                Campaign = campaign, Date = DateTime.Now, SafetyMargin = m,
                Engines = config.Engines, Tests = config.Tests, Seconds = config.Seconds,
                ConfirmSeconds = config.ConfirmSeconds, SoakSeconds = config.SoakSeconds, SoakEngine = config.SoakSeconds > 0 ? config.SoakEngine : null,
                Limits = Enumerable.Range(0, Topology.MaxCores).Select(c => limits.TryGetValue(c, out var l) ? l : null).ToArray(),
            },
        };
        for (var c = 0; c < Topology.MaxCores; c++)
            p.Cores[c] = limits.TryGetValue(c, out var l) && l is { } lim ? Math.Min(config.Top, lim + m) : config.Base;
        return p;
    }
}
