using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Rycolab.Core;

/// <summary><paramref name="Counts"/>: whether the event says something about a core (WHEA 17 is PCIe and does not). <paramref name="ApicId"/>: the logical processor the event names, when it does.</summary>
public sealed record SystemEvent(DateTime Time, string Provider, int Id, string Message, bool Counts = true, int? ApicId = null);

/// <summary>
/// Windows System log: hardware errors (WHEA-Logger 17-20, 46, 47) and power
/// (Kernel-Power 41 unexpected reboot, 42 sleep, 107 resume;
/// Power-Troubleshooter 1 resume). Always queried from a timestamp, never
/// "today": a campaign can cross midnight.
/// </summary>
public static class Whea
{
    private const string WheaProvider = "Microsoft-Windows-WHEA-Logger";
    private const string KernelPower = "Microsoft-Windows-Kernel-Power";
    private const string PowerTroubleshooter = "Microsoft-Windows-Power-Troubleshooter";

    public static readonly int[] HardwareIds = [17, 18, 19, 20, 46, 47];

    /// <summary>Corrected or uncorrected hardware errors that can come from a core, plus unexpected reboots. PCIe errors (id 17) are left out; <see cref="IgnoredSince"/> lists them.</summary>
    public static List<SystemEvent> HardwareSince(DateTime since)
        => Query(since, (WheaProvider, HardwareIds), (KernelPower, [41])).Where(e => e.Counts).ToList();

    /// <summary>The WHEA events in the window that do not count as a core positive, for the record.</summary>
    public static List<SystemEvent> IgnoredSince(DateTime since)
        => Query(since, (WheaProvider, HardwareIds)).Where(e => !e.Counts).ToList();

    /// <summary>
    /// WHEA 17 is a PCIe error (root port or endpoint): the dGPU leaving and
    /// rejoining the bus can raise corrected ones, and none of them says
    /// anything about a core's margin. Everything else WHEA (18-20 machine
    /// check and platform, 46-47 memory) counts; when the event carries an
    /// APIC id it names the logical processor.
    /// </summary>
    internal static (bool Counts, int? ApicId) Interpret(string provider, int id, string xml)
    {
        if (provider != WheaProvider) return (true, null);
        if (id == 17) return (false, null);
        var m = Regex.Match(xml, @"<Data Name=""ApicId"">\s*(0x[0-9A-Fa-f]+|\d+)\s*</Data>");
        if (!m.Success) return (true, null);
        var v = m.Groups[1].Value;
        return (true, v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(v[2..], 16) : int.Parse(v, CultureInfo.InvariantCulture));
    }

    /// <summary>Unexpected reboots only (Kernel-Power 41).</summary>
    public static List<SystemEvent> UnexpectedRebootsSince(DateTime since)
        => Query(since, (KernelPower, [41]));

    /// <summary>Sleep and resume.</summary>
    public static List<SystemEvent> PowerSince(DateTime since)
        => Query(since, (KernelPower, [42, 107]), (PowerTroubleshooter, [1]));

    internal static string XPath(DateTime since, params (string Provider, int[] Ids)[] filters)
    {
        var t = since.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        var alts = filters.Select(f =>
            $"(Provider[@Name='{f.Provider}'] and ({string.Join(" or ", f.Ids.Select(i => $"EventID={i}"))}))");
        return $"*[System[({string.Join(" or ", alts)}) and TimeCreated[@SystemTime>='{t}']]]";
    }

    private static List<SystemEvent> Query(DateTime since, params (string Provider, int[] Ids)[] filters)
    {
        var xpath = XPath(since, filters);
        var list = new List<SystemEvent>();
        try
        {
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath));
            for (var e = reader.ReadEvent(); e is not null; e = reader.ReadEvent())
            {
                using (e)
                {
                    string msg, xml;
                    try { msg = e.FormatDescription() ?? ""; } catch { msg = ""; }
                    try { xml = e.ToXml() ?? ""; } catch { xml = ""; }
                    var (counts, apic) = Interpret(e.ProviderName, e.Id, xml);
                    list.Add(new SystemEvent(e.TimeCreated ?? DateTime.MinValue, e.ProviderName, e.Id,
                        FirstLine(msg) + (apic is { } a ? $" [apic {a}]" : ""), counts, apic));
                }
            }
        }
        catch (EventLogException) { /* no log: return whatever we have */ }
        return list.OrderBy(x => x.Time).ToList();
    }

    private static string FirstLine(string s)
    {
        var i = s.IndexOfAny(['\r', '\n']);
        return (i < 0 ? s : s[..i]).Trim();
    }
}
