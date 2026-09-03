using Rycolab.Core;
using Rycolab.Core.Gpu;

namespace Rycolab.Cli.Commands;

/// <summary>
/// rycolab gpu probe [--all]                 the NVIDIA GPU, its family and its V/F curve with the offsets
/// rycolab gpu show                          the saved profile and whether it is on the curve
/// rycolab gpu import <file> [--profile 1]   an MSI Afterburner profile (.cfg) or a Green Curve config.ini -> gpu-profile.json
/// rycolab gpu set --lock 2655@900 [--below 300]   a profile by hand
/// rycolab gpu apply | off | on              put the profile on the curve (the guard keeps it) | offsets to 0 and disabled | clear a safety lock and apply
/// </summary>
public static class GpuCommand
{
    public static int Run(Args args)
    {
        var sub = args.Positional.FirstOrDefault() ?? "probe";
        return sub switch
        {
            "probe" => Probe(args),
            "show" => Show(),
            "import" => Import(args),
            "set" => Set(args),
            "apply" => Apply(clearLock: false),
            "on" => Apply(clearLock: true),
            "off" => Off(),
            _ => Unknown(sub),
        };
    }

    private static int Unknown(string sub)
    {
        Console.Error.WriteLine($"Unknown gpu command: {sub}. One of: probe, show, import, set, apply, on, off.");
        return 2;
    }

    private static int Probe(Args args)
    {
        using var api = new NvApi();
        if (!api.IsAvailable) { Console.Error.WriteLine($"  No NVIDIA GPU through NvAPI: {api.Unavailable}"); return 1; }
        Console.WriteLine();
        Console.WriteLine($"  {api.Name}   {api.Family} (0x{api.Architecture:X})   device {api.DeviceId:X8} subsystem {api.SubSystemId:X8}");
        VfCurve vf;
        VfPoint[] curve;
        try { vf = new VfCurve(api); curve = vf.Read(); }
        catch (Exception ex) { Console.Error.WriteLine($"  V/F curve: {ex.Message}"); return 1; }
        var data = curve.Count(p => p.HasData);
        var offsets = curve.Count(p => p.OffsetKhz != 0);
        Console.WriteLine($"  backend {vf.Backend.Name}{(vf.Backend.BestGuessOnly ? " (best guess, untested family)" : "")}   {data} points with data, {vf.EditablePoints} editable, {offsets} with an offset");
        var lockAt = VfCurve.DetectLock(curve);
        Console.WriteLine(lockAt is { } l ? $"  curve flat from point {l}: {curve[l].Mv:F1} mV, {curve[l].Mhz} MHz" : "  curve rising to the top: no lock in place");
        if (GpuProfile.Load() is { } p) Console.WriteLine($"  profile {p.Describe}: {(CurveApply.IsApplied(curve, p) ? "on the curve" : "NOT on the curve")}{(p.Enabled ? "" : " (disabled)")}{(p.SafetyLock is { } s ? $"  safety lock: {s}" : "")}");
        Console.WriteLine();
        Console.WriteLine("  point      mV    MHz  offset");
        var every = args.Has("all") ? 1 : 8;
        for (var i = 0; i < VfBackend.Points; i++)
            if (curve[i].HasData && (i % every == 0 || curve[i].OffsetKhz != 0 || i == lockAt || i == VfBackend.LastTailPoint || i == 127))
                Console.WriteLine($"  [{i,3}]  {curve[i].Mv,6:F1}  {curve[i].Mhz,5}  {(curve[i].OffsetKhz == 0 ? "" : $"{curve[i].OffsetKhz / 1000:+0;-0} MHz")}{(i == 127 ? "   (low power)" : "")}");
        Console.WriteLine();
        return 0;
    }

    private static int Show()
    {
        if (GpuProfile.Load() is not { } p) { Console.WriteLine("  No GPU profile. `rycolab gpu import <file>` or `rycolab gpu set --lock 2655@900`."); return 0; }
        Console.WriteLine();
        Console.WriteLine($"  {p.Describe}   {(p.Enabled ? "enabled (the guard keeps it applied)" : "disabled")}{(p.SafetyLock is { } s ? $"   SAFETY LOCK: {s}; `rycolab gpu on` clears it" : "")}");
        if (p.Source is { } src) Console.WriteLine($"  source   {src.Kind}{(src.File is null ? "" : " " + src.File)}, {src.Date:yyyy-MM-dd HH:mm}");
        if (p.Fingerprint is { } f) Console.WriteLine($"  gpu      {f.Name} ({f.Family}, device {f.DeviceId:X8})");
        using var api = new NvApi();
        if (api.IsAvailable)
        {
            try
            {
                var curve = new VfCurve(api).Read();
                Console.WriteLine($"  curve    {(CurveApply.IsApplied(curve, p) ? "the profile is on the curve" : "the profile is NOT on the curve")}");
            }
            catch (Exception ex) { Console.WriteLine($"  curve    unreadable: {ex.Message}"); }
        }
        else Console.WriteLine($"  curve    no GPU on the bus ({api.Unavailable})");
        Console.WriteLine();
        return 0;
    }

    private static int Import(Args args)
    {
        if (args.Positional.Count < 2) { Console.Error.WriteLine("Usage: rycolab gpu import <Afterburner VEN_10DE...cfg | Green Curve config.ini> [--profile 1]"); return 2; }
        var path = Path.GetFullPath(args.Positional[1]);
        if (!File.Exists(path)) { Console.Error.WriteLine($"  Not found: {path}"); return 1; }
        var slot = args.GetInt("profile") ?? 1;
        using var api = new NvApi();
        var profile = new GpuProfile { Source = new GpuProfileSource { File = path, Date = DateTime.Now } };

        if (path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
        {
            if (GreenCurveIni.Read(path, slot) is not { } gc) { Console.Error.WriteLine($"  No lock in profile {slot} of {path}."); return 1; }
            if (!api.IsAvailable) { Console.Error.WriteLine($"  Green Curve stores the lock as a curve index; the GPU is needed to turn it into a voltage ({api.Unavailable})."); return 1; }
            var curve = new VfCurve(api).Read();
            profile.Source.Kind = "greencurve";
            profile.LockMv = (int)Math.Round(curve[gc.LockIndex].Mv);
            profile.LockMhz = gc.LockMhz;
            profile.LowOffsetMhz = gc.GpuOffsetMhz;
        }
        else
        {
            var hex = Afterburner.ReadVfCurve(path, slot);
            if (hex is null) { Console.Error.WriteLine($"  No VFCurve in [Profile{slot}] of {path}."); return 1; }
            List<Afterburner.Point> points;
            try { points = Afterburner.Decode(hex); }
            catch (FormatException ex) { Console.Error.WriteLine($"  {ex.Message}"); return 1; }
            if (Afterburner.Intent(points) is not { } intent) { Console.Error.WriteLine("  The curve keeps rising to the last point: no plateau to lock at."); return 1; }
            profile.Source.Kind = "afterburner";
            (profile.LockMv, profile.LockMhz, profile.LowOffsetMhz) = intent;
            var plateauFrom = points.First(p => p.TargetMhz >= profile.LockMhz - 1);
            Console.WriteLine();
            Console.WriteLine($"  Afterburner profile {slot}: {points.Count} points, plateau {profile.LockMhz} MHz from point {plateauFrom.Index} ({plateauFrom.Mv:F0} mV, base {plateauFrom.BaseMhz:F0} + {plateauFrom.OffsetMhz:+0;-0}); low region {profile.LowOffsetMhz:+0;-0} MHz");
        }

        if (api.IsAvailable) profile.Fingerprint = new GpuFingerprint { Name = api.Name, DeviceId = api.DeviceId, Family = api.Family };
        profile.Save();
        Console.WriteLine($"  Saved {profile.Describe} to {AppPaths.GpuProfile} (disabled). `rycolab gpu apply` puts it on the curve.");
        return 0;
    }

    private static int Set(Args args)
    {
        var lockSpec = args.Get("lock");
        if (lockSpec is null || lockSpec.Split('@') is not [var mhzText, var mvText] || !int.TryParse(mhzText, out var mhz) || !int.TryParse(mvText, out var mv))
        {
            Console.Error.WriteLine("Usage: rycolab gpu set --lock <MHz>@<mV> [--below <MHz offset>]   e.g. --lock 2655@900 --below 300");
            return 2;
        }
        var existing = GpuProfile.Load();
        using var api = new NvApi();
        var profile = new GpuProfile
        {
            LockMhz = mhz, LockMv = mv, LowOffsetMhz = args.GetInt("below") ?? 0,
            Source = new GpuProfileSource { Kind = "manual", Date = DateTime.Now },
            Fingerprint = api.IsAvailable ? new GpuFingerprint { Name = api.Name, DeviceId = api.DeviceId, Family = api.Family } : existing?.Fingerprint,
        };
        profile.Save();
        Console.WriteLine($"  Saved {profile.Describe} to {AppPaths.GpuProfile} (disabled). `rycolab gpu apply` puts it on the curve.");
        return 0;
    }

    private static int Apply(bool clearLock)
    {
        if (GpuProfile.Load() is not { } p) { Console.Error.WriteLine("  No GPU profile. `rycolab gpu import <file>` or `rycolab gpu set --lock ...` first."); return 2; }
        if (p.SafetyLock is { } s && !clearLock) { Console.Error.WriteLine($"  Safety lock: {s}. `rycolab gpu on` clears it and applies."); return 2; }
        using var api = new NvApi();
        if (!api.IsAvailable) { Console.Error.WriteLine($"  No NVIDIA GPU through NvAPI: {api.Unavailable}"); return 1; }
        if (p.Refuse(api) is { } why) { Console.Error.WriteLine($"  Refused: {why}."); return 2; }
        Console.WriteLine();
        Console.WriteLine($"  {api.Name}: applying {p.Describe}");
        var result = CurveApply.Apply(api, new VfCurve(api), p, line => Console.WriteLine($"    {line}"));
        if (!result.Ok)
        {
            Console.Error.WriteLine($"  FAILED: {result.Detail}");
            return 1;
        }
        p.Enabled = true;
        p.SafetyLock = null;
        p.Save();
        Console.WriteLine($"  Applied: {result.Detail}. The guard re-applies it at logon and after sleep; `rycolab gpu off` removes it.");
        Console.WriteLine();
        return 0;
    }

    private static int Off()
    {
        using var api = new NvApi();
        if (GpuProfile.Load() is { } p && p.Enabled) { p.Enabled = false; p.Save(); }
        if (!api.IsAvailable) { Console.WriteLine($"  Profile disabled; no GPU on the bus to reset ({api.Unavailable})."); return 0; }
        var vf = new VfCurve(api);
        var ok = vf.Reset(line => Console.WriteLine($"    {line}"));
        Console.WriteLine(ok ? "  Curve back to the driver's own: every offset 0. Profile disabled." : "  Some offsets did not reset; `rycolab gpu probe` shows them. A reboot returns the driver's curve.");
        return ok ? 0 : 1;
    }
}
