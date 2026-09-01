using System.Diagnostics;
using Rycolab.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Rycolab.Cli.Ui;

/// <summary>
/// The `rycolab status` panel: four sections (Curve Optimizer, Battery,
/// Lenovo EC, Windows) built from data the callers gather. Colors follow
/// GuardView: green ok, red wrong, grey unknown/desaturated. The EC section
/// needs elevation; without it, it degrades to a hint.
/// </summary>
public static class StatusView
{
    private const int LabelWidth = 12;

    private static Grid NewGrid(int labelWidth = LabelWidth)
    {
        var g = new Grid();
        g.AddColumn(new GridColumn().NoWrap().PadRight(2).Width(labelWidth));
        g.AddColumn();
        return g;
    }

    private static void KV(Grid g, string label, string valueMarkup)
        => g.AddRow(new Markup($"[grey]{Markup.Escape(label)}[/]"), new Markup(valueMarkup));

    private static Panel Section(string title, IRenderable body)
        => new(body) { Header = new PanelHeader($" [bold]{title}[/] "), Border = BoxBorder.Rounded, Expand = true, Padding = new Padding(1, 0, 1, 0) };

    private static string E(string? s) => Markup.Escape(s ?? "-");

    // ---- Curve Optimizer -------------------------------------------------

    public static IRenderable Co(Process? guard, State? state, Rycolab.Core.Profile? profile)
    {
        var g = NewGrid();
        KV(g, "guard", guard is { } p
            ? $"[green]RUNNING[/]  pid {p.Id}, since {p.StartTime:HH:mm:ss}"
            : "[red]not running[/]  (`rycolab on` applies and guards the profile)");
        KV(g, "profile", profile is null ? "[red]none[/]" : E(Commands.StatusCommand.Describe(profile)));

        if (state is not null)
        {
            var v = state.ValidationStartedAt is { } vs
                ? $"  [grey](since {vs:yyyy-MM-dd}: {state.GuardedSeconds / 3600.0:F1} h guarded, {state.Resumes} resumes, {state.Reapplies} re-applies,[/] {Count(state.Whea, "WHEA")}[grey],[/] {Count(state.Resets, "resets")}[grey])[/]"
                : "";
            KV(g, "phase", $"[bold]{E(state.Phase)}[/]{v}");
            if (state.LastTick is { } t)
                KV(g, "last sample", $"{t:yyyy-MM-dd HH:mm:ss}  {E(state.LastState)}  CPU {state.CpuLoad?.ToString("F0") ?? "-"} %  package {state.PackagePower?.ToString("F1") ?? "-"} W");

            if (profile is not null || state.Hardware is { Length: > 0 })
            {
                var count = state.Hardware?.Length is > 0 and var n ? n
                    : profile?.Fingerprint?.Cores is > 0 and var f ? f : Topology.MaxCores;
                var off = 0;
                for (var first = 0; first < count; first += Topology.CoresPerCcd)
                {
                    var cells = new List<string>();
                    for (var c = first; c < Math.Min(count, first + Topology.CoresPerCcd); c++)
                    {
                        var want = profile is not null && c < profile.Cores.Length ? profile.Cores[c] : (int?)null;
                        var hw = state.Hardware is { } h && c < h.Length ? h[c] : null;
                        var color = hw is null ? "grey" : want is null || hw == want ? "default" : "red";
                        if (hw is not null && want is not null && hw != want) off++;
                        cells.Add($"[{color}]{(hw?.ToString() ?? "-"),4}[/]");
                    }
                    KV(g, first == 0 ? "hardware" : "", $"[grey]{Topology.CcdNameFromIndex(first / Topology.CoresPerCcd)}[/] {string.Join(" ", cells)}");
                }
                KV(g, "", off == 0 && guard is not null && state.Applied
                    ? "[green]all cores on profile[/]"
                    : off > 0 ? $"[red]{off} cores off profile[/]" : "[grey]baseline (profile not applied)[/]");
            }

            if (state.LastError is { } err) KV(g, "last error", $"[red]{E(err)}[/]");
            foreach (var (e, i) in state.LastEvents.TakeLast(5).Select((e, i) => (e, i)))
                KV(g, i == 0 ? "events" : "", $"[grey]{E(e.Length > 110 ? e[..110] + "..." : e)}[/]");
        }
        return Section("Curve Optimizer", g);
    }

    private static string Count(int v, string label) => v == 0 ? $"[green]0 {label}[/]" : $"[red]{v} {label}[/]";

    /// <summary>The mode list only changes with the resolution; enumerate once per process.</summary>
    private static readonly Lazy<string> AvailableRates = new(() => string.Join(",", WindowsPower.AvailableRefreshRates()));

    private static bool _loadPrimed;

    /// <summary>Design capacity and cycle count move daily at most; the live view repaints every 2 s.</summary>
    private static readonly Lazy<(double? DesignWh, int? Cycles)> HealthStatics = new(() =>
    {
        var s = BatteryHealth.Read();
        return (s.DesignWh, s.Cycles);
    });

    // ---- Battery ---------------------------------------------------------

    public static IRenderable Battery(State? state)
    {
        var g = NewGrid();
        var b = BatteryInfo.Read();
        KV(g, "line", b.OnAc is { } ac
            ? (ac ? "[green]AC[/]" : $"[yellow]battery[/]  {b.DischargeW?.ToString("F1") ?? "-"} W  {b.Percent?.ToString("F0") ?? "-"} %  {b.RemainingWh?.ToString("F1") ?? "-"} Wh{(b.HoursLeft is { } h ? $"  [grey]~{h:F1} h at this rate[/]" : "")}")
            : "[grey]?[/]");

        var top = ProcessLoad.Top(4);
        var primed = _loadPrimed;
        _loadPrimed = true;
        // A single LibreHardwareMonitor sample occasionally returns garbage (373 W seen on 01/09); no attribution beyond the physical ceiling.
        var pkg = state?.GuardPid is not null && state.PackagePower is > 0 and < 250 ? state.PackagePower : null;
        KV(g, "cpu top", top.Count == 0
            ? (primed ? "[grey]everything under 0.5 % CPU[/]" : "[grey]sampling...[/]")
            : string.Join("   ", top.Select(t =>
                // Below ~5 % CPU the "share" is mostly the idle/uncore floor, not the process; no watt figure there.
                $"{E(t.Name.Length > 16 ? t.Name[..16] : t.Name)} {t.CpuPct:F1}[grey]%[/]{(pkg is { } w && t.CpuPct >= 5 ? $" [grey]~{t.BusyShare * w:F0} W[/]" : "")}")));

        var (design, cycles) = HealthStatics.Value;
        KV(g, "health", b.FullWh is { } fw
            ? $"{fw:F1} Wh full charge{(design is > 0 and var dw ? $"  [grey]{100.0 * fw / dw:F1} % of {dw:F1} Wh design[/]" : "")}{(cycles is { } cy ? $"  [grey]{cy} cycles[/]" : "")}"
            : "[grey]?[/]");

        var snap = PowerSnapshot.Load();
        var byGuard = state?.PowerProfile == "battery" ? " [grey](applied by the guard)[/]" : "";
        KV(g, "profile", snap is { } s
            ? $"[green]applied[/] at {s.TakenAt:HH:mm:ss}{byGuard}  [grey](`power ac` restores {E(LenovoEc.ModeName(s.Mode))}, {E(LenovoEc.IGpuModeName(s.IGpuMode))}, {s.Hz?.ToString() ?? "?"} Hz)[/]"
            : "[grey]not applied[/]");

        var plan = Plan.LoadOrDefault();
        KV(g, "auto", plan.PowerAuto
            ? $"[green]on[/]  [grey]{E(plan.PowerAutoOptions.ToString())}; battery profile {Guard.AcDebounceSeconds} s after unplugging, restored on AC[/]"
            : "[grey]off[/]  (`rycolab power auto on` lets the guard handle it)");

        KV(g, "panel", $"{WindowsPower.RefreshHz?.ToString() ?? "?"} Hz  [grey](available {AvailableRates.Value})[/]  brightness {WindowsPower.Brightness?.ToString() ?? "?"} %");
        return Section("Battery", g);
    }

    // ---- Lenovo EC -------------------------------------------------------

    /// <summary>Pass null instances when not elevated; the callers own their lifetime (the live view reuses them across refreshes).</summary>
    public static IRenderable Ec(LenovoEc? ec, LenovoEnergy? energy)
    {
        var g = NewGrid();
        if (ec is null)
        {
            KV(g, "", "[grey]run elevated (`sudo rycolab status`) to read the EC: power mode and limits, GPU mode, fans, charge mode[/]");
            return Section("Lenovo EC", g);
        }
        if (!ec.IsAvailable)
        {
            KV(g, "", "[grey]no Lenovo EC on this machine[/]");
            return Section("Lenovo EC", g);
        }
        KV(g, "power mode", $"[bold]{E(LenovoEc.ModeName(ec.SmartFanMode))}[/]  [grey]{E(LenovoEc.Describe(ec.PowerLimits))}[/]");
        var present = LenovoEc.DgpuPresent();
        KV(g, "gpu", $"{E(LenovoEc.IGpuModeName(ec.IGpuMode))}  dGPU {(present ? "present" : "[green]off[/]")}");
        var full = ec.FanFullSpeed;
        KV(g, "fans", $"CPU {ec.CpuFanRpm?.ToString() ?? "-"}  GPU {ec.GpuFanRpm?.ToString() ?? "-"}  PCH {ec.PchFanRpm?.ToString() ?? "-"} RPM   full speed {(full is { } f ? (f ? "[yellow]ON[/]" : "off") : "?")}");
        KV(g, "EC temps", $"CPU {ec.CpuTempC?.ToString() ?? "-"}  GPU {ec.GpuTempC?.ToString() ?? "-"}  PCH {ec.PchTempC?.ToString() ?? "-"} C");
        if (energy is { IsAvailable: true })
        {
            var mode = energy.ChargeMode();
            var night = energy.NightCharge();
            KV(g, "charge", $"{E(mode)}{(mode == LenovoEnergy.Conservation ? "  [grey](stops at ~80 %)[/]" : "")}{(ChargeFull.Load() is { } cf ? $"  [yellow]full charge -> {cf.Restore} at {cf.Target} %[/]" : "")}   night charge {(night is { } n ? (n ? "on" : "off") : "n/a")}");
        }
        return Section("Lenovo EC", g);
    }

    // ---- Windows ---------------------------------------------------------

    public static IRenderable Windows()
    {
        var g = NewGrid(22);
        var (oac, odc) = WindowsPower.Overlays();
        KV(g, "slider", $"AC {E(oac)}  [grey]/[/]  battery {E(odc)}");
        var snapActive = PowerSnapshot.Load() is not null;
        foreach (var (sub, setting, label, battery) in WindowsPower.DcSettings)
        {
            if (WindowsPower.Query(sub, setting) is not { } q) continue;
            var dc = snapActive && q.Dc == battery ? $"[green]{q.Dc}[/]" : q.Dc.ToString();
            KV(g, label, $"AC {q.Ac}   DC {dc}");
        }
        return Section("Windows", g);
    }
}
