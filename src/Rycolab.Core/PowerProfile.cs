using System.Diagnostics;
using System.Management;
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
                    // A recently active card sometimes never leaves after the switch
                    // (reproducible: query it awake right before). The cure is Legion
                    // Toolkit's EnsureDGPUEjected: notifying the EC that the card is
                    // still there makes it RETRY the ejection - but it only lands once
                    // the card has idled for ~2-3 min (three timed cures on
                    // 2026-09-01; nothing pnputil does speeds that up). A short round
                    // here, then a marker: the guard nudges the EC every tick until
                    // the node LEAVES the bus, and disables it as a paltry last
                    // resort (a disabled node is silicon with power and no driver,
                    // ~20 W measured).
                    if (g == LenovoEc.IGpuOnly && present)
                    {
                        present = NotifyRetry(ec, 3, log);
                        if (present)
                        {
                            new DgpuEject(DateTime.Now).Save();
                            log("dGPU still present; the guard keeps nudging the EC (a busy card ejects only after ~2-3 min of idle)");
                        }
                    }
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

        // A pending ejection is over either way once AC is back.
        DgpuEject.Delete();
        // If the guard's last resort disabled the stuck dGPU node, bring it
        // back before the mode switch so the driver reloads with it. Checked
        // unconditionally: idempotent, and it also cleans up a manual disable.
        Dgpu("enable-device", log, onlyIfDisabled: true);

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

    /// <summary>
    /// Legion Toolkit's EnsureDGPUEjected: NotifyDGPUStatus(1) makes the EC
    /// retry the ejection; checks every 5 s. Returns whether the card is
    /// still present after the attempts.
    /// </summary>
    private static bool NotifyRetry(LenovoEc ec, int attempts, Action<string> log)
    {
        for (var i = 1; i <= attempts; i++)
        {
            ec.NotifyDgpuStatus(true);
            Thread.Sleep(5000);
            if (!LenovoEc.DgpuPresent()) { log($"dGPU ejected after notify retry {i}"); return false; }
        }
        log($"dGPU still present after {attempts} notify retries");
        return true;
    }

    /// <summary>
    /// Runs `pnputil /&lt;verb&gt; &lt;instanceId&gt;` on the NVIDIA display node
    /// (restart-device / disable-device / enable-device). pnputil rather than
    /// WMI Enable/Disable: the WMI invoke threw NullReferenceException on
    /// 2026-09-01, and ArgumentList sidesteps the quoting of the `&amp;` in the
    /// instance id that broke the shell attempts. Silent no-op without an
    /// NVIDIA device; false when nothing was done or pnputil failed.
    /// </summary>
    internal static bool Dgpu(string verb, Action<string> log, bool onlyIfDisabled = false)
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\cimv2",
                "SELECT PNPDeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPClass = 'Display' AND Name LIKE '%NVIDIA%'");
            foreach (ManagementObject m in s.Get())
            {
                if (onlyIfDisabled && Convert.ToInt32(m["ConfigManagerErrorCode"]) != 22) return false;
                var id = (string)m["PNPDeviceID"];
                var psi = new ProcessStartInfo("pnputil.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                psi.ArgumentList.Add($"/{verb}");
                psi.ArgumentList.Add(id);
                var p = Process.Start(psi)!;
                p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                p.WaitForExit(30000);
                var ok = p.ExitCode == 0;
                log($"dGPU node {verb}{(ok ? "" : $" FAILED (pnputil exit {p.ExitCode})")}");
                return ok;
            }
        }
        catch (Exception ex) { log($"dGPU node {verb} failed: {ex.Message}"); }
        return false;
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
