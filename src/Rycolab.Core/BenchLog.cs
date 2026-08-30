using System.Globalization;
using System.Text;

namespace Rycolab.Core;

/// <summary>
/// The CSV `rycolab dev log` writes and `rycolab report --bench` reads: one
/// row per sample with what a benchmark comparison needs (package power,
/// temperatures, effective clocks, core voltages, fans). Invariant culture,
/// comma separated, empty cell when a source is unavailable.
/// </summary>
public static class BenchLog
{
    public const string Time = "time";
    public const string Elapsed = "elapsed_s";
    public const string PackagePower = "pkg_w";
    public const string Tctl = "tctl_c";
    public const string Ccd0 = "ccd0_c";
    public const string Ccd1 = "ccd1_c";
    public const string EffAvg = "eff_avg_mhz";
    public const string VoltAvg = "vcore_avg_v";
    public const string VoltMax = "vcore_max_v";
    public const string VidAvg = "vid_avg_v";
    public const string CoreTempMax = "core_temp_max_c";
    public const string CpuFan = "fan_cpu_rpm";
    public const string GpuFan = "fan_gpu_rpm";
    public const string PchFan = "fan_pch_rpm";
    public const string EcCpu = "ec_cpu_c";
    public const string EcGpu = "ec_gpu_c";
    public const string EcPch = "ec_pch_c";
    public const string Ac = "ac";                 // 1 on the mains, 0 on battery
    public const string BatteryW = "bat_w";        // discharge rate, empty on AC
    public const string BatteryPct = "bat_pct";
    public const string BatteryWh = "bat_wh";

    public static string Eff(int core) => $"eff_c{core}_mhz";
    public static string Volt(int core) => $"vcore_c{core}_v";

    public static List<string> Columns(int coreCount)
    {
        var cols = new List<string> { Time, Elapsed, PackagePower, Tctl, Ccd0, Ccd1, EffAvg, VoltAvg, VoltMax, VidAvg, CoreTempMax, CpuFan, GpuFan, PchFan, EcCpu, EcGpu, EcPch, Ac, BatteryW, BatteryPct, BatteryWh };
        cols.AddRange(Enumerable.Range(0, coreCount).Select(Eff));
        cols.AddRange(Enumerable.Range(0, coreCount).Select(Volt));
        return cols;
    }

    public static string Cell(double? v, int decimals) => v?.ToString("F" + decimals, CultureInfo.InvariantCulture) ?? "";
    public static string Cell(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";

    /// <summary>Column name -> values of the rows that pass the filter (cells that do not parse are skipped).</summary>
    public static Dictionary<string, List<double>> Read(string path, Func<Dictionary<string, double>, bool>? rowFilter, out int rows, out int kept)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        var lines = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        rows = 0; kept = 0;
        if (lines.Length == 0) throw new InvalidDataException($"{path} is empty");
        var header = lines[0].Split(',');
        var data = header.ToDictionary(h => h, _ => new List<double>());
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0) continue;
            rows++;
            var cells = line.Split(',');
            var row = new Dictionary<string, double>();
            for (var i = 0; i < header.Length && i < cells.Length; i++)
                if (double.TryParse(cells[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) row[header[i]] = d;
            if (rowFilter is not null && !rowFilter(row)) continue;
            kept++;
            foreach (var (k, v) in row) data[k].Add(v);
        }
        return data;
    }

    public sealed record Stat(double Mean, double Max, double Min, int N);

    public static Stat? Of(Dictionary<string, List<double>> d, string col)
        => d.TryGetValue(col, out var xs) && xs.Count > 0 ? new Stat(xs.Average(), xs.Max(), xs.Min(), xs.Count) : null;

    private static readonly (string Col, string Label, int Dec)[] Rows =
    [
        (PackagePower, "Package power [W]", 1), (Tctl, "Tctl [C]", 1), (Ccd0, "CCD0 Tdie [C]", 1), (Ccd1, "CCD1 Tdie [C]", 1),
        (CoreTempMax, "Hottest core (PM table) [C]", 1), (EffAvg, "Effective clock, all cores [MHz]", 0),
        (VoltAvg, "Core voltage avg (PM table) [V]", 4), (VoltMax, "Core voltage max (PM table) [V]", 4), (VidAvg, "VID avg (LHM) [V]", 4),
        (CpuFan, "CPU fan [RPM]", 0), (GpuFan, "GPU fan [RPM]", 0), (PchFan, "PCH fan [RPM]", 0),
        (EcCpu, "EC CPU temp [C]", 0), (EcGpu, "EC GPU temp [C]", 0), (EcPch, "EC PCH temp [C]", 0),
        (BatteryW, "Battery discharge [W]", 2), (BatteryPct, "Battery charge [%]", 1),
    ];

    /// <summary>Markdown table of the aggregates; with a baseline, a delta column. <paramref name="filter"/> names the row filter applied.</summary>
    public static string Summary(string name, Dictionary<string, List<double>> d, int rows, int kept, string filter,
        string? baseName = null, Dictionary<string, List<double>>? b = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Samples with {filter}: {kept} of {rows}{(b is not null ? $" ({baseName}: {Of(b, Elapsed)?.N ?? 0})" : "")}.");
        sb.AppendLine();
        if (Of(d, BatteryW) is { Mean: > 0 } w && FullWh(d) is > 0 and var full)
        {
            var bw = b is not null ? Of(b, BatteryW) : null;
            sb.AppendLine($"Runtime at the mean discharge from a full battery ({full:F0} Wh): {full / w.Mean:F1} h" +
                          (bw is { Mean: > 0 } ? $" ({baseName}: {full / bw.Mean:F1} h; power {(w.Mean - bw.Mean) / bw.Mean * 100:+0.0;-0.0} %)" : "") + ".");
            sb.AppendLine();
        }
        sb.AppendLine(b is null ? "| Sensor | mean | max | min |" : $"| Sensor | {baseName} mean | {name} mean | delta | {name} max |");
        sb.AppendLine(b is null ? "|---|---|---|---|" : "|---|---|---|---|---|");
        foreach (var (col, label, dec) in Rows)
        {
            var s = Of(d, col);
            if (s is null) continue;
            if (b is null)
                sb.AppendLine($"| {label} | {F(s.Mean, dec)} | {F(s.Max, dec)} | {F(s.Min, dec)} |");
            else
            {
                var bs = Of(b, col);
                var delta = bs is null ? "-" : F(s.Mean - bs.Mean, dec, sign: true);
                sb.AppendLine($"| {label} | {(bs is null ? "-" : F(bs.Mean, dec))} | {F(s.Mean, dec)} | {delta} | {F(s.Max, dec)} |");
            }
        }
        return sb.ToString();
    }

    /// <summary>Full-charge capacity implied by the samples: Wh / (pct/100), median to dodge rounding.</summary>
    private static double FullWh(Dictionary<string, List<double>> d)
    {
        if (!d.TryGetValue(BatteryWh, out var wh) || !d.TryGetValue(BatteryPct, out var pct) || wh.Count == 0 || pct.Count == 0) return 0;
        var xs = wh.Zip(pct).Where(p => p.Second > 0).Select(p => p.First * 100.0 / p.Second).OrderBy(x => x).ToList();
        return xs.Count == 0 ? 0 : xs[xs.Count / 2];
    }

    private static string F(double v, int dec, bool sign = false)
        => (sign && v > 0 ? "+" : "") + v.ToString("F" + dec, CultureInfo.InvariantCulture);
}
