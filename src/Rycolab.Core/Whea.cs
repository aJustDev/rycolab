using System.Diagnostics.Eventing.Reader;
using System.Globalization;

namespace Rycolab.Core;

public sealed record SystemEvent(DateTime Time, string Provider, int Id, string Message);

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

    /// <summary>Corrected or uncorrected hardware errors, plus unexpected reboots.</summary>
    public static List<SystemEvent> HardwareSince(DateTime since)
        => Query(since, (WheaProvider, HardwareIds), (KernelPower, [41]));

    /// <summary>Sleep and resume.</summary>
    public static List<SystemEvent> PowerSince(DateTime since)
        => Query(since, (KernelPower, [42, 107]), (PowerTroubleshooter, [1]));

    private static List<SystemEvent> Query(DateTime since, params (string Provider, int[] Ids)[] filters)
    {
        var t = since.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        var alts = filters.Select(f =>
            $"(Provider[@Name='{f.Provider}'] and ({string.Join(" or ", f.Ids.Select(i => $"EventID={i}"))}))");
        var xpath = $"*[System[({string.Join(" or ", alts)}) and TimeCreated[@SystemTime>='{t}']]]";

        var list = new List<SystemEvent>();
        try
        {
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath));
            for (var e = reader.ReadEvent(); e is not null; e = reader.ReadEvent())
            {
                using (e)
                {
                    string msg;
                    try { msg = e.FormatDescription() ?? ""; } catch { msg = ""; }
                    list.Add(new SystemEvent(e.TimeCreated ?? DateTime.MinValue, e.ProviderName, e.Id, FirstLine(msg)));
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
