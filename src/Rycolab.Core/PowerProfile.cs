using System.Diagnostics;
using System.Text.Json;

namespace Rycolab.Core;

/// <summary>What `power battery` changes. Saved in config.json as PowerAuto options too.</summary>
public sealed class PowerOptions
{
    /// <summary>igpu | auto | keep</summary>
    public string Gpu { get; set; } = "igpu";
    /// <summary>quiet | keep (the EC power mode)</summary>
    public string Mode { get; set; } = "quiet";
    public int? Hz { get; set; } = 60;
    public int? Brightness { get; set; } = 40;
    public bool Windows { get; set; } = true;
    public bool CloseApps { get; set; }

    public override string ToString()
        => $"mode {Mode}, gpu {Gpu}, {(Hz is { } h ? h + " Hz" : "Hz kept")}, {(Brightness is { } b ? "brightness " + b + " %" : "brightness kept")}, {(Windows ? "DC scheme" : "no DC scheme")}{(CloseApps ? ", close apps" : "")}";
}

/// <summary>Everything the battery profile touched, taken before the first change; `ac` restores from it.</summary>
public sealed class PowerSnapshot
{
    public DateTime TakenAt { get; set; }
    public int? Mode { get; set; }
    public int? IGpuMode { get; set; }
    public int? Hz { get; set; }
    public int? Brightness { get; set; }
    public Dictionary<string, int> Dc { get; set; } = [];   // "sub/setting" -> DC index

    public static string Path => System.IO.Path.Combine(AppPaths.Data, "power-prev.json");
    public static PowerSnapshot? Load() => Journal.ReadJsonFile<PowerSnapshot>(Path);
    public void Save() => Journal.WriteJsonFile(Path, this);
    public static void Delete() { try { File.Delete(Path); } catch { } }
}

/// <summary>
/// The battery profile: EC quiet mode, iGPU-only, 60 Hz, dimmer panel, the
/// DC values of the Windows scheme; and the way back. Each knob is applied
/// once, read back and reported through <paramref name="log"/>; a knob that
/// fails does not stop the others. Shared by `rycolab power` and the guard.
/// </summary>
public static class PowerProfile
{
    // Afterburner and RTSS poll the dGPU continuously and keep resetting its
    // idle timer on battery; RTSS survives Afterburner and must go too.
    public static readonly string[] BackgroundApps = ["LenovoLegionToolkit", "HWiNFO64", "MSIAfterburner", "RTSS"];

    /// <summary>Returns the number of knobs that failed.</summary>
    public static int Battery(LenovoEc ec, PowerOptions o, Action<string> log)
    {
        var failed = 0;
        var snap = PowerSnapshot.Load();
        if (snap is null)
        {
            snap = new PowerSnapshot { TakenAt = DateTime.Now, Mode = ec.SmartFanMode, IGpuMode = ec.IGpuMode, Hz = WindowsPower.RefreshHz, Brightness = WindowsPower.Brightness };
            foreach (var (sub, setting, _, _) in WindowsPower.DcSettings)
                if (WindowsPower.Query(sub, setting) is { } q) snap.Dc[$"{sub}/{setting}"] = q.Dc;
            snap.Save();
            log($"snapshot: mode {LenovoEc.ModeName(snap.Mode)}, gpu {LenovoEc.IGpuModeName(snap.IGpuMode)}, {snap.Hz?.ToString() ?? "?"} Hz, brightness {snap.Brightness?.ToString() ?? "?"} %, {snap.Dc.Count} DC settings");
        }
        else log($"snapshot from {snap.TakenAt:HH:mm:ss} kept (battery profile already applied once; `power ac` restores it)");

        // EC power mode: quiet. The limits it runs with are printed, never written.
        if (o.Mode != "quiet") log($"power mode kept ({LenovoEc.ModeName(ec.SmartFanMode)})");
        else if (ec.SmartFanMode is { } m && m != LenovoEc.QuietMode)
        {
            var after = ec.SetSmartFanMode(LenovoEc.QuietMode);
            var ok = after == LenovoEc.QuietMode;
            if (!ok) failed++;
            log($"power mode {LenovoEc.ModeName(m)} -> {LenovoEc.ModeName(after)}{(ok ? "" : " (FAILED)")}; limits in effect: {LenovoEc.Describe(ec.PowerLimits)}");
            Thread.Sleep(2000);
        }
        else log($"power mode already quiet; limits in effect: {LenovoEc.Describe(ec.PowerLimits)}");

        // iGPU mode. After the switch the EC wants to know whether the dGPU node has gone (Toolkit: NotifyDGPUStatus, 5 x 5 s).
        var wantGpu = o.Gpu switch { "igpu" => LenovoEc.IGpuOnly, "auto" => LenovoEc.IGpuAuto, _ => (int?)null };
        if (wantGpu is { } g)
        {
            var before = ec.IGpuMode;
            if (before == g) log($"gpu already {LenovoEc.IGpuModeName(g)}");
            else
            {
                var after = ec.SetIGpuMode(g);
                if (after != g) { failed++; log($"gpu {LenovoEc.IGpuModeName(before)} -> {LenovoEc.IGpuModeName(after)} (FAILED)"); }
                else
                {
                    var present = WaitDgpu(false, 25, log);
                    ec.NotifyDgpuStatus(present);
                    log($"gpu {LenovoEc.IGpuModeName(before)} -> {LenovoEc.IGpuModeName(after)}; dGPU {(present ? "still present (notified 1)" : "gone (notified 0)")}");
                }
            }
        }

        if (o.Hz is { } hz)
        {
            var before = WindowsPower.RefreshHz;
            if (before == hz) log($"panel already {hz} Hz");
            else
            {
                var after = WindowsPower.SetRefreshHz(hz);
                if (after != hz) failed++;
                log($"panel {before?.ToString() ?? "?"} -> {after?.ToString() ?? "?"} Hz{(after == hz ? "" : $" (FAILED; available: {string.Join(",", WindowsPower.AvailableRefreshRates())})")}");
            }
        }

        if (o.Brightness is { } b)
        {
            var before = WindowsPower.Brightness;
            var after = WindowsPower.SetBrightness(b);
            if (after != b) failed++;
            log($"brightness {before?.ToString() ?? "?"} -> {after?.ToString() ?? "?"} %{(after == b ? "" : " (FAILED)")}");
        }

        if (o.Windows)
        {
            foreach (var (sub, setting, label, value) in WindowsPower.DcSettings)
            {
                var q = WindowsPower.Query(sub, setting);
                if (q is null) { log($"DC {label}: not exposed by powercfg, skipped"); continue; }
                if (q.Value.Dc == value) { log($"DC {label} already {value}"); continue; }
                var after = WindowsPower.SetDc(sub, setting, value);
                if (after != value) failed++;
                log($"DC {label} {q.Value.Dc} -> {after?.ToString() ?? "?"}{(after == value ? "" : " (FAILED)")}");
            }
        }

        if (o.CloseApps)
        {
            foreach (var name in BackgroundApps)
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { p.Kill(); p.WaitForExit(5000); log($"closed {name} (pid {p.Id})"); }
                    catch (Exception ex) { failed++; log($"could not close {name}: {ex.Message}"); }
                }
        }
        return failed;
    }

    /// <summary>Back to the snapshot. With <paramref name="force"/> every value is written even if it looks untouched.</summary>
    public static int Ac(LenovoEc ec, Action<string> log, bool force = false)
    {
        var snap = PowerSnapshot.Load();
        if (snap is null) { log("no battery profile applied (no snapshot); nothing to restore"); return 0; }
        var failed = 0;

        if (snap.Dc.Count > 0)
            foreach (var (key, dc) in snap.Dc)
            {
                var parts = key.Split('/');
                var label = WindowsPower.DcSettings.FirstOrDefault(s => s.Sub == parts[0] && s.Setting == parts[1]).Label ?? key;
                var now = WindowsPower.Query(parts[0], parts[1])?.Dc;
                if (now == dc && !force) continue;
                var after = WindowsPower.SetDc(parts[0], parts[1], dc);
                if (after != dc) failed++;
                log($"DC {label} {now?.ToString() ?? "?"} -> {after?.ToString() ?? "?"}{(after == dc ? "" : " (FAILED)")}");
            }

        if (snap.Brightness is { } b && (force || WindowsPower.Brightness != b))
        {
            var after = WindowsPower.SetBrightness(b);
            if (after != b) failed++;
            log($"brightness -> {after?.ToString() ?? "?"} %{(after == b ? "" : " (FAILED)")}");
        }

        if (snap.Hz is { } hz && (force || WindowsPower.RefreshHz != hz))
        {
            var after = WindowsPower.SetRefreshHz(hz);
            if (after != hz) failed++;
            log($"panel -> {after?.ToString() ?? "?"} Hz{(after == hz ? "" : " (FAILED)")}");
        }

        if (snap.IGpuMode is { } g && (force || ec.IGpuMode != g))
        {
            var after = ec.SetIGpuMode(g);
            if (after != g) { failed++; log($"gpu -> {LenovoEc.IGpuModeName(after)} (FAILED)"); }
            else
            {
                var present = WaitDgpu(true, 25, log);
                ec.NotifyDgpuStatus(present);
                log($"gpu -> {LenovoEc.IGpuModeName(after)}; dGPU {(present ? "back (notified 1)" : "NOT back yet (notified 0)")}");
            }
            Thread.Sleep(2000);
        }

        if (snap.Mode is { } m && (force || ec.SmartFanMode != m))
        {
            var after = ec.SetSmartFanMode(m);
            if (after != m) failed++;
            log($"power mode -> {LenovoEc.ModeName(after)}{(after == m ? "" : " (FAILED)")}; limits in effect: {LenovoEc.Describe(ec.PowerLimits)}");
        }

        if (failed == 0) PowerSnapshot.Delete();
        else log("snapshot kept because something failed; `power restore` retries");
        return failed;
    }

    private static bool WaitDgpu(bool wantPresent, int seconds, Action<string> log)
    {
        var t0 = DateTime.Now;
        while ((DateTime.Now - t0).TotalSeconds < seconds)
        {
            if (LenovoEc.DgpuPresent() == wantPresent) return wantPresent;
            Thread.Sleep(1000);
        }
        log($"dGPU did not {(wantPresent ? "come back" : "leave")} within {seconds} s");
        return LenovoEc.DgpuPresent();
    }
}
