using System.Diagnostics;
using Rycolab.Core;
using Rycolab.Core.Legion;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Rycolab.Cli.Ui;

/// <summary>
/// The `rycolab status` panel. By default one verdict line and a few rows
/// (profile, hardware, events, battery): what is applied and whether it is
/// right. `--all` adds the Lenovo EC, the processes burning the CPU and the
/// Windows scheme, each in its own panel. Colors: green ok, red wrong,
/// yellow attention, grey unknown or secondary.
/// </summary>
public static class StatusView
{
    private const int LabelWidth = 10;

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

    /// <summary>What Spectre will wrap at (80 when the output is redirected).</summary>
    private static int Width() => Math.Max(60, AnsiConsole.Profile.Width);

    private static string Guarded(long seconds)
        => seconds < 48 * 3600 ? $"{seconds / 3600.0:F1} h guarded" : $"{seconds / 86400.0:F1} d guarded";

    private static string Version => typeof(Guard).Assembly.GetName().Version?.ToString(3) ?? "?";

    // ---- the default view --------------------------------------------------

    /// <summary>Header line, the Curve Optimizer panel (verdict first) and the Battery panel. What `rycolab` and `rycolab status` show without `--all`.</summary>
    public static IRenderable Summary(Process? guard, State? state, Rycolab.Core.Profile? profile)
        => new Rows(
            new Markup($"  [grey]rycolab {Version}   {DateTime.Now:yyyy-MM-dd HH:mm:ss}[/]\n"),
            Co(guard, state, profile),
            Battery(state));

    public static IRenderable Co(Process? guard, State? state, Rycolab.Core.Profile? profile)
    {
        var g = NewGrid();
        g.AddRow(new Markup(""), new Markup(Verdict(guard, state, profile)));
        KV(g, "profile", profile is null ? "[red]none[/]" : E(Commands.StatusCommand.Describe(profile)));

        if (state is not null)
        {
            Hardware(g, guard, state, profile);
            if (state.LastError is { } err) KV(g, "last error", $"[red]{E(err)}[/]");
            var room = Width() - LabelWidth - 8;
            foreach (var (e, i) in state.LastEvents.TakeLast(3).Select((e, i) => (e, i)))
                KV(g, i == 0 ? "events" : "", $"[grey]{E(e.Length > room ? e[..Math.Max(0, room - 3)] + "..." : e)}[/]");
        }
        return Section("Curve Optimizer", g);
    }

    public static IRenderable Battery(State? state)
    {
        var g = NewGrid();
        var b = BatteryInfo.Read();
        if (b.OnAc is null)
        {
            KV(g, "", "[grey]no battery on this machine[/]");
            return Section("Battery", g);
        }
        KV(g, "line", b.OnAc == true
            ? "[green]AC[/]"
            : $"[yellow]battery[/]  {b.DischargeW?.ToString("F1") ?? "-"} W  {b.Percent?.ToString("F0") ?? "-"} %  {b.RemainingWh?.ToString("F1") ?? "-"} Wh{(b.HoursLeft is { } h ? $"  [grey]~{h:F1} h at this rate[/]" : "")}");

        var (design, cycles) = HealthStatics.Value;
        KV(g, "health", b.FullWh is { } fw
            ? $"{fw:F1} Wh full charge{(design is > 0 and var dw ? $"   [grey]{100.0 * fw / dw:F0} % of {dw:F1} Wh design[/]" : "")}{(cycles is { } cy ? $"   [grey]{cy} cycles[/]" : "")}"
            : "[grey]?[/]");

        var plan = Plan.LoadOrDefault();
        var snap = PowerSnapshot.Load();
        var byGuard = state?.PowerProfile == "battery" ? " by the guard" : "";
        var auto = plan.PowerAuto
            ? $"   [grey]power auto on: battery profile {Guard.AcDebounceSeconds} s after unplugging, restored on AC[/]"
            : "   [grey]power auto off (`rycolab legion power auto on`)[/]";
        KV(g, "profile", snap is { } s
            ? $"[green]applied[/]{byGuard} at {s.TakenAt:HH:mm}  [grey](`rycolab legion power ac` restores)[/]{auto}"
            : $"not applied{auto}");
        return Section("Battery", g);
    }

    /// <summary>One line, in color: is the profile on the cores right now, and in what phase.</summary>
    private static string Verdict(Process? guard, State? state, Rycolab.Core.Profile? profile)
    {
        if (profile is null) return "[yellow bold]NO PROFILE[/]";
        var applied = guard is not null && state is { Applied: true };
        var head = applied ? "[green bold]PROFILE APPLIED[/]" : "[red bold]PROFILE NOT APPLIED[/]";
        if (state is null) return $"{head}   [grey]no guard state yet[/]";

        var phase = state.Phase == "positive" && state.Positive is { } why ? $"positive ({why})" : state.Phase;
        var who = guard is { } p
            ? $"guard pid {p.Id} since {p.StartTime:HH:mm}"
            : state.LastTick is { } t ? $"guard stopped, last sample {t:HH:mm:ss}" : "guard not running";
        var counters = state.ValidationStartedAt is not null
            ? $"   {Guarded(state.GuardedSeconds)}   {Count(state.Whea, "WHEA")}   {Count(state.Resets, "resets")}"
            : "";
        var detail = !applied && guard is not null && state.LastState is { } ls && ls != "ok" ? $"   [yellow]{E(ls)}[/]" : "";
        return $"{head}   [bold]{E(phase)}[/]   {E(who)}{counters}{detail}";
    }

    /// <summary>One row when every core is on profile; the per-CCD rows with the wrong ones in red otherwise.</summary>
    private static void Hardware(Grid g, Process? guard, State state, Rycolab.Core.Profile? profile)
    {
        var sample = state.LastTick is { } t
            ? $"   [grey]last sample {t:HH:mm:ss}   CPU {state.CpuLoad?.ToString("F0") ?? "-"} %   package {state.PackagePower?.ToString("F1") ?? "-"} W[/]"
            : "";
        if (state.Hardware is not { Length: > 0 } hw)
        {
            KV(g, "hardware", $"[grey]no reading yet[/]{sample}");
            return;
        }

        var off = 0;
        var rows = new List<string>();
        foreach (var ccd in Enumerable.Range(0, hw.Length).GroupBy(Topology.CcdOf).OrderBy(g => g.Key))
        {
            var cells = new List<string>();
            foreach (var c in ccd)
            {
                var want = profile is not null && c < profile.Cores.Length ? profile.Cores[c] : (int?)null;
                var color = hw[c] is null ? "grey" : want is null || hw[c] == want ? "default" : "red";
                if (hw[c] is not null && want is not null && hw[c] != want) off++;
                cells.Add($"[{color}]{(hw[c]?.ToString() ?? "-"),4}[/]");
            }
            rows.Add($"[grey]{Topology.CcdNameFromIndex(ccd.Key)}[/] {string.Join(" ", cells)}");
        }

        if (off == 0 && guard is not null && state.Applied)
        {
            KV(g, "hardware", $"[green]all {hw.Count(x => x is not null)} cores on profile[/]{sample}");
            return;
        }
        for (var i = 0; i < rows.Count; i++) KV(g, i == 0 ? "hardware" : "", rows[i]);
        KV(g, "", (off > 0 ? $"[red]{off} cores off profile[/]" : "[grey]baseline (profile not applied)[/]") + sample);
    }

    private static string Count(int v, string label) => v == 0 ? $"[green]0 {label}[/]" : $"[red]{v} {label}[/]";

    /// <summary>Design capacity and cycle count move daily at most; the live view repaints every 2 s.</summary>
    private static readonly Lazy<(double? DesignWh, int? Cycles)> HealthStatics = new(() =>
    {
        var s = BatteryHealth.Read();
        return (s.DesignWh, s.Cycles);
    });

    // ---- the `--all` panels -------------------------------------------------

    /// <summary>The mode list only changes with the resolution; enumerate once per process.</summary>
    private static readonly Lazy<string> AvailableRates = new(() => string.Join(",", WindowsPower.AvailableRefreshRates()));

    private static bool _loadPrimed;

    /// <summary>Who burns the CPU (between two samples) and the panel. Belongs to the machine, not to the profile.</summary>
    public static IRenderable Machine(State? state)
    {
        var g = NewGrid();
        var top = ProcessLoad.Top(4);
        var primed = _loadPrimed;
        _loadPrimed = true;
        var pkg = state?.GuardPid is not null && state.PackagePower is > 0 and < 250 ? state.PackagePower : null;
        KV(g, "cpu top", top.Count == 0
            ? (primed ? "[grey]everything under 0.5 % CPU[/]" : "[grey]sampling...[/]")
            : string.Join("   ", top.Select(t =>
                // Below ~5 % CPU the "share" is mostly the idle/uncore floor, not the process; no watt figure there.
                $"{E(t.Name.Length > 16 ? t.Name[..16] : t.Name)} {t.CpuPct:F1}[grey]%[/]{(pkg is { } w && t.CpuPct >= 5 ? $" [grey]~{t.BusyShare * w:F0} W[/]" : "")}")));
        KV(g, "panel", $"{WindowsPower.RefreshHz?.ToString() ?? "?"} Hz  [grey](available {AvailableRates.Value})[/]  brightness {WindowsPower.Brightness?.ToString() ?? "?"} %");
        var plan = Plan.LoadOrDefault();
        if (plan.PowerAuto) KV(g, "power auto", $"[grey]{E(plan.PowerAutoOptions.ToString())}[/]");
        return Section("Machine", g);
    }

    /// <summary>Pass null instances when not elevated; the callers own their lifetime (the live view reuses them across refreshes).</summary>
    public static IRenderable Ec(LenovoEc? ec, LenovoEnergy? energy)
    {
        var g = NewGrid();
        if (ec is null)
        {
            KV(g, "", "[grey]run elevated (`sudo rycolab status --all`) to read the EC: power mode and limits, GPU mode, fans, charge mode[/]");
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

    public static IRenderable Windows()
    {
        var g = NewGrid(22);
        var (oac, odc) = WindowsPower.Overlays();
        KV(g, "slider", $"AC {E(oac)}  [grey]/[/]  battery {E(odc)}");
        var snapActive = PowerSnapshot.Load() is not null;
        foreach (var (sub, setting, label, battery) in WindowsPower.DcSettings)
        {
            if (WindowsPower.Query(sub, setting) is not { } q) continue;
            var dc = $"{E(WindowsPower.DcName(setting, q.Dc))} [grey]({q.Dc})[/]";
            if (snapActive && q.Dc == battery) dc = $"[green]{dc}[/]";
            KV(g, label, $"AC {E(WindowsPower.DcName(setting, q.Ac))} [grey]({q.Ac})[/]   DC {dc}");
        }
        return Section("Windows", g);
    }
}
