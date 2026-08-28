using System.Text.Json;

namespace LegionCoLab.Core;

/// <summary>
/// plan.json: el perfil por nucleo que se quiere llevar puesto y los
/// parametros del barrido que lo produce. Un solo fichero para guard, sweep
/// y report; validado contra Safety antes de usarse.
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

    /// <summary>Raiz del repositorio: el primer directorio hacia arriba que tenga src/ y docs/.</summary>
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
        if (!File.Exists(path)) throw new FileNotFoundException($"no existe el plan {path}");
        var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(path), Json)
                   ?? throw new InvalidDataException($"plan vacio: {path}");
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
            throw new SafetyViolationException($"el perfil tiene {Profile.Length} valores; hacen falta {Topology.MaxCores}.");
        Safety.ValidateMargins(Profile);
        Safety.ValidateMargin(Base, "base");
        Safety.ValidateMargin(Start, "inicio");
        Safety.ValidateMargin(Top, "tope");
        if (Step <= 0) throw new SafetyViolationException("el paso tiene que ser positivo.");
        if (Start > Top) throw new SafetyViolationException($"inicio {Start} por encima del tope {Top}.");
        if (Seconds <= 0) throw new SafetyViolationException("seconds tiene que ser positivo.");
        if (SafetyMargin < 0) throw new SafetyViolationException("safetyMargin no puede ser negativo.");
    }

    public string YCruncherDir => Path.IsPathRooted(YCruncher) ? YCruncher : Path.Combine(RepoRoot, YCruncher);

    /// <summary>Nucleos cuya lectura no coincide con el perfil.</summary>
    public List<int> Mismatches(IReadOnlyList<CoreReading> readings)
        => readings.Where(r => r.Index < Profile.Length && r.Margin != Profile[r.Index]).Select(r => r.Index).ToList();

    public IReadOnlyList<(int Core, int Margin)> Targets(int coreCount)
        => Enumerable.Range(0, Math.Min(coreCount, Profile.Length)).Select(c => (c, Profile[c])).ToList();
}
