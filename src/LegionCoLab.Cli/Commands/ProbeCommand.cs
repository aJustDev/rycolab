using System.Text.Json;
using LegionCoLab.Core;

namespace LegionCoLab.Cli.Commands;

public static class ProbeCommand
{
    private const string LltProfile = @"%LOCALAPPDATA%\LenovoLegionToolkit\amd_overclocking.json";

    public static int Run(Args args)
    {
        using var co = new CoController();

        var readings = co.ReadAll();
        var expected = args.Has("no-compare")
            ? null
            : LoadProfile(Environment.ExpandEnvironmentVariables(args.Get("compare", LltProfile)));

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

        // ---- cabecera ----
        Console.WriteLine();
        Console.WriteLine($"  CPU                {co.CpuName}");
        Console.WriteLine($"  Nucleos fisicos    {co.PhysicalCores}");
        Console.WriteLine($"  Tipo SMU           {co.SmuType}");
        Console.WriteLine($"  SetDldoPsmMargin   {(co.IsPsmSupported ? "soportado" : "NO SOPORTADO — el Curve Optimizer no puede aplicarse")}");
        if (co.TryGetFMax() is { } fmax) Console.WriteLine($"  FMax               {fmax}");
        Console.WriteLine($"  Momento            {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        // ---- tabla ----
        Console.WriteLine();
        var hasCores = cores is not null;
        Console.WriteLine($"  Nucleo  CCD   mascara       perfil   HARDWARE{(hasCores ? "  reloj ef.   pot." : "")}   ");
        Console.WriteLine($"  ------  ----  ------------  -------  --------{(hasCores ? "  ---------  -----" : "")}   ------------------");

        var mismatched = new List<int>();
        var matched = 0;
        var readable = 0;

        foreach (var r in readings)
        {
            var exp = expected is not null && r.Index < expected.Length ? expected[r.Index] : null;
            var note = "";

            if (r.IsReadable) readable++;
            else note = "sin lectura (nucleo inactivo?)";

            if (r.Margin is { } hw && exp is { } e)
            {
                if (hw == e) { matched++; note = "coincide"; }
                else { mismatched.Add(r.Index); note = "NO COINCIDE"; }
            }

            var extra = "";
            if (cores is not null && r.Index < cores.Count)
            {
                var c = cores[r.Index];
                extra = "  " + (c.ClockEffective?.ToString("F0") ?? "—").PadLeft(9)
                      + "  " + (c.Power?.ToString("F2") ?? "—").PadLeft(5);
            }

            Console.WriteLine("  {0,6}  {1,-4}  0x{2:X8}    {3,7}  {4,8}{5}   {6}",
                r.Index, r.Ccd, r.Mask,
                exp?.ToString() ?? "-",
                r.Margin?.ToString() ?? "-",
                extra,
                note);
        }

        // ---- resumen ----
        Console.WriteLine();
        Console.WriteLine($"  Leidos del hardware: {readable} de {readings.Count}");

        var byCcd = readings
            .Where(r => r.IsReadable)
            .GroupBy(r => r.Ccd)
            .Select(g => $"{g.Key} {Describe(g.Select(x => x.Margin!.Value))}");
        Console.WriteLine($"  Aplicado ahora mismo: {string.Join("   ", byCcd)}");

        if (snap is { } s)
        {
            Console.WriteLine();
            Console.WriteLine($"  Paquete {Fmt(s.PackagePower, "W", 1)}   Tctl {Fmt(s.Tctl, "C", 1)}   " +
                              $"CCD1 {Fmt(s.Ccd1Temp, "C", 1)}   CCD2 {Fmt(s.Ccd2Temp, "C", 1)}   " +
                              $"reloj medio {Fmt(s.MaxCoreClock, "MHz", 0)}");
        }
        else if (args.Has("sensors"))
        {
            Console.WriteLine($"  (sin telemetria: {telemetry?.Unavailable ?? "no disponible"})");
        }

        if (mismatched.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  NO COINCIDEN los nucleos: {string.Join(", ", mismatched)}");
            Console.WriteLine("  El perfil en disco NO es lo que tiene el procesador.");
        }
        else if (matched > 0)
        {
            Console.WriteLine($"  Perfil y hardware coinciden en los {matched} nucleos comparables.");
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
            Console.WriteLine($"  Guardado en {Path.GetFullPath(jsonPath)}");
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
        => v.HasValue ? v.Value.ToString($"F{decimals}") + " " + unit : "—";

    private static int?[]? LoadProfile(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"  (sin perfil que comparar: no existe {path})");
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("CoreValues", out var cv) || cv.ValueKind != JsonValueKind.Array)
                return null;

            var list = new List<int?>();
            foreach (var e in cv.EnumerateArray())
                list.Add(e.ValueKind == JsonValueKind.Number ? e.GetInt32() : null);
            return list.ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (no se pudo leer el perfil: {ex.Message})");
            return null;
        }
    }
}
