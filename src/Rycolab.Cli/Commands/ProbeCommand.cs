using System.Text.Json;
using Rycolab.Core;

namespace Rycolab.Cli.Commands;

public static class ProbeCommand
{
    public static int Run(Args args)
    {
        using var co = new CoController();

        var readings = co.ReadAll();

        // By default compare with the installed profile if there is one.
        int?[]? expected = null;
        var compareLabel = "";
        if (!args.Has("no-compare"))
        {
            if (args.Get("compare") is { } cmp)
            {
                expected = LoadProfile(Environment.ExpandEnvironmentVariables(cmp));
                compareLabel = cmp;
            }
            else if (Profile.Exists())
            {
                expected = Profile.Load().Cores.Select(m => (int?)m).ToArray();
                compareLabel = AppPaths.Profile;
            }
        }
        var baseline = Plan.LoadOrDefault().Base;

        TelemetrySnapshot? snap = null;
        IReadOnlyList<CoreSample>? cores = null;
        Telemetry? telemetry = null;
        if (args.Has("sensors"))
        {
            telemetry = new Telemetry();
            if (telemetry.IsAvailable)
            {
                snap = telemetry.Read();
                cores = telemetry.AllCores(co.CoreCount);
            }
        }

        // ---- header ----
        Console.WriteLine();
        Console.WriteLine($"  CPU                {co.CpuName}");
        Console.WriteLine($"  Physical cores     {co.PhysicalCores}");
        Console.WriteLine($"  SMU type           {co.SmuType}");
        Console.WriteLine($"  SetDldoPsmMargin   {(co.IsPsmSupported ? "supported" : "NOT SUPPORTED - Curve Optimizer cannot be applied")}");
        if (co.TryGetFMax() is { } fmax) Console.WriteLine($"  FMax               {fmax}");
        Console.WriteLine($"  Time               {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        if (expected is not null) Console.WriteLine($"  Comparing with     {compareLabel}");

        // ---- table ----
        Console.WriteLine();
        var hasCores = cores is not null;
        Console.WriteLine($"  Core    CCD   mask          profile  HARDWARE{(hasCores ? "  eff. clk   power" : "")}   ");
        Console.WriteLine($"  ------  ----  ------------  -------  --------{(hasCores ? "  ---------  -----" : "")}   ------------------");

        var mismatched = new List<int>();
        var matched = 0;
        var readable = 0;

        foreach (var r in readings)
        {
            var exp = expected is not null && r.Index < expected.Length ? expected[r.Index] : null;
            var note = "";

            if (r.IsReadable) readable++;
            else note = "no reading (inactive core?)";

            if (r.Margin is { } hw && exp is { } e)
            {
                if (hw == e) { matched++; note = "match"; }
                else { mismatched.Add(r.Index); note = "MISMATCH"; }
            }

            var extra = "";
            if (cores is not null && r.Index < cores.Count)
            {
                var c = cores[r.Index];
                extra = "  " + (c.ClockEffective?.ToString("F0") ?? "-").PadLeft(9)
                      + "  " + (c.Power?.ToString("F2") ?? "-").PadLeft(5);
            }

            Console.WriteLine("  {0,6}  {1,-4}  0x{2:X8}    {3,7}  {4,8}{5}   {6}",
                r.Index, r.Ccd, r.Mask,
                exp?.ToString() ?? "-",
                r.Margin?.ToString() ?? "-",
                extra,
                note);
        }

        // ---- summary ----
        Console.WriteLine();
        Console.WriteLine($"  Read from hardware: {readable} of {readings.Count}");

        var byCcd = readings
            .Where(r => r.IsReadable)
            .GroupBy(r => r.Ccd)
            .Select(g => $"{g.Key} {Describe(g.Select(x => x.Margin!.Value))}");
        Console.WriteLine($"  Applied right now: {string.Join("   ", byCcd)}");

        if (snap is { } s)
        {
            Console.WriteLine();
            Console.WriteLine($"  Package {Fmt(s.PackagePower, "W", 1)}   Tctl {Fmt(s.Tctl, "C", 1)}   " +
                              $"CCD0 {Fmt(s.Ccd0Temp, "C", 1)}   CCD1 {Fmt(s.Ccd1Temp, "C", 1)}   " +
                              $"average clock {Fmt(s.MaxCoreClock, "MHz", 0)}");
        }
        else if (args.Has("sensors"))
        {
            Console.WriteLine($"  (no telemetry: {telemetry?.Unavailable ?? "unavailable"})");
        }

        if (mismatched.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  MISMATCH on cores: {string.Join(", ", mismatched)}");
            Console.WriteLine("  The profile on disk is NOT what the processor has.");
            if (readings.All(r => r.Margin == baseline))
                Console.WriteLine($"  Everything is at the baseline {baseline}: no guard, or reboot/sleep. `rycolab on` puts the profile back.");
        }
        else if (matched > 0)
        {
            Console.WriteLine($"  The processor has the profile applied: all {matched} comparable cores match.");
        }
        Console.WriteLine();

        if (args.Get("json") is { } jsonPath)
        {
            Journal.WriteJsonFile(Path.GetFullPath(jsonPath), new
            {
                timestamp = DateTime.Now,
                cpu = co.CpuName,
                smuType = co.SmuType,
                psmSupported = co.IsPsmSupported,
                fMax = co.TryGetFMax(),
                psm = readings.Select(r => new { core = r.Index, ccd = r.Ccd, mask = r.Mask, margin = r.Margin }),
                expected,
                mismatched,
                telemetry = snap,
                perCore = cores,
            });
            Console.WriteLine($"  Saved to {Path.GetFullPath(jsonPath)}");
            Console.WriteLine();
        }

        telemetry?.Dispose();
        return mismatched.Count > 0 ? 2 : 0;
    }

    private static string Describe(IEnumerable<int> margins)
    {
        var distinct = margins.Distinct().OrderBy(x => x).ToList();
        return distinct.Count == 1 ? distinct[0].ToString() : string.Join("/", distinct);
    }

    private static string Fmt(double? v, string unit, int decimals)
        => v.HasValue ? v.Value.ToString($"F{decimals}") + " " + unit : "-";

    /// <summary>A rycolab profile file, or a JSON with a CoreValues array (Legion Toolkit format).</summary>
    private static int?[]? LoadProfile(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"  (nothing to compare with: {path} does not exist)");
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("cores", out var cores) && cores.ValueKind == JsonValueKind.Array)
                return cores.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number ? e.GetInt32() : (int?)null).ToArray();
            if (doc.RootElement.TryGetProperty("CoreValues", out var cv) && cv.ValueKind == JsonValueKind.Array)
                return cv.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number ? e.GetInt32() : (int?)null).ToArray();
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (could not read the profile: {ex.Message})");
            return null;
        }
    }
}
