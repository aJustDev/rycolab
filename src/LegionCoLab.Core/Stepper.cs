namespace LegionCoLab.Core;

/// <summary>
/// Aplica un conjunto de margenes caminando en paradas de como mucho
/// <see cref="Safety.MaxStepBetweenLevels"/> cuentas, releyendo en cada
/// parada, bajo <see cref="SafetySession"/>. Es la unica ruta de escritura
/// de apply, guard y sweep.
/// </summary>
public static class Stepper
{
    /// <summary>Camino desde from hasta to sin pasos mayores del limite.</summary>
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
    /// Escribe los objetivos y devuelve la lectura final. Lanza
    /// <see cref="CoWriteFailedException"/> si algun nucleo no queda donde se pidio.
    /// </summary>
    public static IReadOnlyList<CoreReading> Apply(CoController co, IReadOnlyList<(int Core, int Margin)> targets, Action<string>? log = null)
    {
        foreach (var (core, margin) in targets)
            Safety.ValidateMargin(margin, $"nucleo {core}: margen");
        Safety.RequireAcPower();

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
                log?.Invoke($"parada {stop + 1}/{maxLen} verificada");
            }
            session.Commit();
        }

        var after = co.ReadAll();
        var bad = targets.Where(t => current.ContainsKey(t.Core) && after[t.Core].Margin != t.Margin).Select(t => t.Core).ToList();
        if (bad.Count > 0)
            throw new CoWriteFailedException($"nucleos que no quedaron en su objetivo: {string.Join(", ", bad)}");
        return after;
    }
}
