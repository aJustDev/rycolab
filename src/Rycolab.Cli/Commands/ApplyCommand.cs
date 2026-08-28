using System.Text.Json;
using Rycolab.Core;

namespace Rycolab.Cli.Commands;

/// <summary>
/// Applies Curve Optimizer margins.
///
/// Never in one jump: the move is walked in stops of at most
/// <see cref="Safety.MaxStepBetweenLevels"/> counts, reading back at every
/// stop, so the max-step rule holds without chaining commands by hand.
/// </summary>
public static class ApplyCommand
{
    public static int Run(Args args)
    {
        using var co = new CoController();

        if (!co.IsPsmSupported)
        {
            Console.Error.WriteLine("This SMU does not support SetDldoPsmMargin. Nothing can be applied.");
            return 1;
        }

        var targets = ResolveTargets(args, co);
        if (targets is null) return 2;

        var dryRun = args.Has("dry-run");

        // Limits are checked BEFORE touching the hardware, so an absurd value
        // dies here and not in the SMU mailbox.
        foreach (var (core, margin) in targets)
            Safety.ValidateMargin(margin, $"core {core}: margin");

        if (!dryRun) Safety.RequireAcPower();

        var current = co.ReadAll().Where(r => r.Margin.HasValue)
                        .ToDictionary(r => r.Index, r => r.Margin!.Value);

        // ---- plan ----
        Console.WriteLine();
        Console.WriteLine("  Core    CCD   now     ->  target     path");
        Console.WriteLine("  ------  ----  -----      --------   ------------------");

        var plans = new List<(int Core, int[] Path)>();
        var changing = 0;

        foreach (var (core, target) in targets.OrderBy(t => t.Core))
        {
            if (!current.TryGetValue(core, out var from))
            {
                Console.WriteLine($"  {core,6}  {Topology.CcdName(core),-4}      -      {target,8}   no reading, skipped");
                continue;
            }

            var path = Stepper.BuildPath(from, target);
            if (path.Length == 0)
            {
                Console.WriteLine($"  {core,6}  {Topology.CcdName(core),-4}  {from,5}      {target,8}   already there");
                continue;
            }

            changing++;
            plans.Add((core, path));
            Console.WriteLine($"  {core,6}  {Topology.CcdName(core),-4}  {from,5}      {target,8}   {string.Join(" -> ", path)}");
        }

        Console.WriteLine();

        if (changing == 0)
        {
            Console.WriteLine("  Nothing to change.");
            Console.WriteLine();
            return 0;
        }

        if (dryRun)
        {
            Console.WriteLine("  --dry-run: nothing was written.");
            Console.WriteLine();
            return 0;
        }

        // ---- write, under the safety net ----
        using var session = new SafetySession(co);

        var maxLen = plans.Max(p => p.Path.Length);
        for (var stop = 0; stop < maxLen; stop++)
        {
            foreach (var (core, path) in plans)
            {
                if (stop >= path.Length) continue;
                co.WriteCore(core, path[stop]);   // WriteCore reads back and throws on mismatch
            }

            if (maxLen > 1)
                Console.WriteLine($"  stop {stop + 1}/{maxLen} verified");
        }

        session.Commit();

        // ---- independent final verification ----
        var after = co.ReadAll();
        var bad = targets.Where(t => after.FirstOrDefault(r => r.Index == t.Core).Margin != t.Margin).ToList();

        Console.WriteLine();
        foreach (var g in after.Where(r => r.IsReadable).GroupBy(r => r.Ccd))
            Console.WriteLine($"  {g.Key}: {string.Join(", ", g.Select(x => x.Margin!.Value).Distinct().OrderBy(x => x))}");

        Console.WriteLine();
        if (bad.Count > 0)
        {
            Console.Error.WriteLine($"  FAILED: {bad.Count} cores did not reach their target.");
            return 2;
        }

        Console.WriteLine($"  Applied and verified on {changing} cores.");
        Console.WriteLine();
        return 0;
    }

    private static List<(int Core, int Margin)>? ResolveTargets(Args args, CoController co)
    {
        if (args.Has("plan"))
            return Plan.Load(args.Get("plan")).Targets(co.CoreCount).ToList();

        if (args.Get("profile") is { } profilePath)
        {
            var path = Environment.ExpandEnvironmentVariables(profilePath);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Profile not found: {path}");
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("CoreValues", out var cv) || cv.ValueKind != JsonValueKind.Array)
            {
                Console.Error.WriteLine("The profile has no CoreValues array.");
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
            Console.Error.WriteLine("Missing --margin (or --profile / --plan).");
            return null;
        }

        if (args.GetInt("core") is { } core)
        {
            if (core < 0 || core >= co.CoreCount)
            {
                Console.Error.WriteLine($"--core {core} out of range (0..{co.CoreCount - 1}).");
                return null;
            }
            return [(core, margin)];
        }

        if (args.GetInt("ccd") is { } ccd)
        {
            if (ccd is not (0 or 1))
            {
                Console.Error.WriteLine("--ccd must be 0 or 1.");
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
