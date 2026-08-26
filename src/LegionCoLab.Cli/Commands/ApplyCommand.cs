using System.Text.Json;
using LegionCoLab.Core;

namespace LegionCoLab.Cli.Commands;

/// <summary>
/// Aplica margenes de Curve Optimizer.
///
/// El movimiento nunca es de un salto: se camina en tramos de como mucho
/// <see cref="Safety.MaxStepBetweenLevels"/> cuentas, releyendo en cada parada.
/// Asi la regla del paso maximo se cumple sin obligar a encadenar ordenes a mano.
/// </summary>
public static class ApplyCommand
{
    public static int Run(Args args)
    {
        using var co = new CoController();

        if (!co.IsPsmSupported)
        {
            Console.Error.WriteLine("Este SMU no soporta SetDldoPsmMargin. No se puede aplicar nada.");
            return 1;
        }

        var targets = ResolveTargets(args, co);
        if (targets is null) return 2;

        var dryRun = args.Has("dry-run");

        // El tope se comprueba ANTES de tocar el hardware, para que un valor
        // absurdo muera aqui y no en el buzon SMU.
        foreach (var (core, margin) in targets)
            Safety.ValidateMargin(margin, $"nucleo {core}: margen");

        if (!dryRun) Safety.RequireAcPower();

        var current = co.ReadAll().Where(r => r.Margin.HasValue)
                        .ToDictionary(r => r.Index, r => r.Margin!.Value);

        // ---- plan ----
        Console.WriteLine();
        Console.WriteLine("  Nucleo  CCD   ahora   ->  objetivo   camino");
        Console.WriteLine("  ------  ----  -----      --------   ------------------");

        var plans = new List<(int Core, int[] Path)>();
        var changing = 0;

        foreach (var (core, target) in targets.OrderBy(t => t.Core))
        {
            if (!current.TryGetValue(core, out var from))
            {
                Console.WriteLine($"  {core,6}  {Topology.CcdName(core),-4}      —      {target,8}   sin lectura, se omite");
                continue;
            }

            var path = BuildPath(from, target);
            if (path.Length == 0)
            {
                Console.WriteLine($"  {core,6}  {Topology.CcdName(core),-4}  {from,5}      {target,8}   ya esta");
                continue;
            }

            changing++;
            plans.Add((core, path));
            Console.WriteLine($"  {core,6}  {Topology.CcdName(core),-4}  {from,5}      {target,8}   {string.Join(" -> ", path)}");
        }

        Console.WriteLine();

        if (changing == 0)
        {
            Console.WriteLine("  Nada que cambiar.");
            Console.WriteLine();
            return 0;
        }

        if (dryRun)
        {
            Console.WriteLine("  --dry-run: no se ha escrito nada.");
            Console.WriteLine();
            return 0;
        }

        // ---- escritura, bajo red ----
        using var session = new SafetySession(co);

        var maxLen = plans.Max(p => p.Path.Length);
        for (var stop = 0; stop < maxLen; stop++)
        {
            foreach (var (core, path) in plans)
            {
                if (stop >= path.Length) continue;
                co.WriteCore(core, path[stop]);   // WriteCore relee y lanza si no coincide
            }

            if (maxLen > 1)
                Console.WriteLine($"  parada {stop + 1}/{maxLen} verificada");
        }

        session.Commit();

        // ---- verificacion final, independiente ----
        var after = co.ReadAll();
        var bad = targets.Where(t => after.FirstOrDefault(r => r.Index == t.Core).Margin != t.Margin).ToList();

        Console.WriteLine();
        foreach (var g in after.Where(r => r.IsReadable).GroupBy(r => r.Ccd))
            Console.WriteLine($"  {g.Key}: {string.Join(", ", g.Select(x => x.Margin!.Value).Distinct().OrderBy(x => x))}");

        Console.WriteLine();
        if (bad.Count > 0)
        {
            Console.Error.WriteLine($"  FALLO: {bad.Count} nucleos no quedaron en su objetivo.");
            return 2;
        }

        Console.WriteLine($"  Aplicado y verificado en {changing} nucleos.");
        Console.WriteLine();
        return 0;
    }

    /// <summary>Camino desde <paramref name="from"/> hasta <paramref name="to"/> sin pasos mayores del limite.</summary>
    private static int[] BuildPath(int from, int to)
    {
        if (from == to) return [];

        var path = new List<int>();
        var step = Safety.MaxStepBetweenLevels;
        var cur = from;

        while (cur != to)
        {
            var remaining = to - cur;
            var move = Math.Sign(remaining) * Math.Min(Math.Abs(remaining), step);
            cur += move;
            path.Add(cur);
        }

        return [.. path];
    }

    private static List<(int Core, int Margin)>? ResolveTargets(Args args, CoController co)
    {
        if (args.Get("profile") is { } profilePath)
        {
            var path = Environment.ExpandEnvironmentVariables(profilePath);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"No existe el perfil {path}");
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("CoreValues", out var cv) || cv.ValueKind != JsonValueKind.Array)
            {
                Console.Error.WriteLine("El perfil no tiene un array CoreValues.");
                return null;
            }

            var list = new List<(int, int)>();
            var i = 0;
            foreach (var e in cv.EnumerateArray())
            {
                if (i >= co.CoreCount) break;
                if (e.ValueKind == JsonValueKind.Number) list.Add((i, e.GetInt32()));
                i++;
            }
            return list;
        }

        if (args.GetInt("margin") is not { } margin)
        {
            Console.Error.WriteLine("Falta --margin (o --profile).");
            return null;
        }

        if (args.GetInt("core") is { } core)
        {
            if (core < 0 || core >= co.CoreCount)
            {
                Console.Error.WriteLine($"--core {core} fuera de rango (0..{co.CoreCount - 1}).");
                return null;
            }
            return [(core, margin)];
        }

        if (args.GetInt("ccd") is { } ccd)
        {
            if (ccd is not (0 or 1))
            {
                Console.Error.WriteLine("--ccd tiene que ser 0 o 1 (numeracion de Legion Toolkit).");
                return null;
            }
            var first = Topology.FirstCoreOfCcd(ccd);
            return Enumerable.Range(first, Topology.CoresPerCcd)
                             .Where(c => c < co.CoreCount)
                             .Select(c => (c, margin))
                             .ToList();
        }

        return Enumerable.Range(0, co.CoreCount).Select(c => (c, margin)).ToList();
    }
}
