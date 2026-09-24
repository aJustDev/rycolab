using System.Globalization;
using System.Text.Json.Serialization;

namespace Rycolab.Core.Gpu;

public sealed class GpuFingerprint
{
    public string Name { get; set; } = "";
    public uint DeviceId { get; set; }
    public string Family { get; set; } = "";
}

public sealed class GpuProfileSource
{
    /// <summary>afterburner | greencurve | manual</summary>
    public string Kind { get; set; } = "manual";
    public string? File { get; set; }
    public DateTime Date { get; set; }
}

/// <summary>
/// What the user wants the GPU's V/F curve to be, in Afterburner's terms:
/// a static frequency offset at the lock point (<see cref="LockOffsetMhz"/>
/// at <see cref="LockMv"/>), the tail flattened to it, and a uniform offset
/// below (capped so nothing rises above the lock). Offsets, not a target
/// clock: the driver's base curve sits in two states on the reference
/// machine (idle and awake, ~260 MHz apart), and an offset computed
/// against the idle base put the awake lock 240 MHz too high and into a
/// TDR loop (2026-09-03). A static offset rides the base the way
/// Afterburner's does; <see cref="LockMhz"/> and <see cref="LockBaseMhz"/>
/// only record what the offset meant where it was derived. gpu-profile.json.
/// The guard keeps it applied while <see cref="Enabled"/>; a TDR sets
/// <see cref="SafetyLock"/> and the guard stops until `gpu on`.
/// </summary>
public sealed class GpuProfile
{
    public bool Enabled { get; set; }
    public int LockMv { get; set; }
    /// <summary>The offset on the lock point, MHz. The source of truth.</summary>
    public int LockOffsetMhz { get; set; }
    /// <summary>The clock the lock meant, and the base it was derived against (both informational).</summary>
    public int LockMhz { get; set; }
    public int LockBaseMhz { get; set; }
    public int LowOffsetMhz { get; set; }
    /// <summary>
    /// How the points above the lock are written: "floor" (Green Curve's
    /// Blackwell way: every tail point at the driver's minimum offset, the
    /// lock point sets the ceiling) or "points" (Afterburner's way: each
    /// tail point gets the offset that puts it at the lock's clock, the
    /// default). The driver honoured both on the reference machine and Time
    /// Spy could not tell them apart (2026-09-04: 20449 floor, 20342 points,
    /// 20069 stock; the GPU sits at its power limit under the lock).
    /// </summary>
    public string Tail { get; set; } = "points";
    [JsonIgnore] public bool TailPerPoint => string.Equals(Tail, "points", StringComparison.OrdinalIgnoreCase);
    public string? SafetyLock { get; set; }
    public GpuFingerprint? Fingerprint { get; set; }
    public GpuProfileSource? Source { get; set; }

    [JsonIgnore] public string Describe =>
        $"{LockOffsetMhz:+0;-0} MHz at {LockMv} mV{(LockMhz > 0 ? $" ({LockMhz} MHz on a {LockBaseMhz} base)" : "")}{(LowOffsetMhz != 0 ? $", {LowOffsetMhz:+0;-0} MHz below" : "")}{(TailPerPoint ? ", tail per point" : "")}";

    public static bool Exists() => File.Exists(AppPaths.GpuProfile);
    public static GpuProfile? Load() => Journal.ReadJsonFile<GpuProfile>(AppPaths.GpuProfile);
    public void Save() => Journal.WriteJsonFile(AppPaths.GpuProfile, this);

    /// <summary>Null when the profile can be applied to this GPU; the reason otherwise.</summary>
    public string? Refuse(NvApi api)
    {
        if (LockMv <= 0 || (LockOffsetMhz == 0 && LockMhz <= 0)) return "the profile has no lock";
        if (Fingerprint is { } f && f.DeviceId != 0 && f.DeviceId != api.DeviceId) return $"the profile is for {f.Name} (device {f.DeviceId:X8}), this is {api.Name} ({api.DeviceId:X8})";
        return null;
    }
}

/// <summary>
/// MSI Afterburner's profile file (`Profiles\VEN_10DE&amp;DEV_...cfg`): the
/// `VFCurve=` value of a `[ProfileN]` section is hex, format 2: an 8-byte
/// header (00 00 02 00, then the point count) and one triplet of floats per
/// point: offset MHz, mV, base MHz at the time the profile was saved.
/// Decoded from the reference machine's profile on 2026-09-03; the target of
/// a point is base + offset.
/// </summary>
public static class Afterburner
{
    public sealed record Point(int Index, double OffsetMhz, double Mv, double BaseMhz)
    {
        public double TargetMhz => BaseMhz + OffsetMhz;
    }

    public static string? ReadVfCurve(string cfgPath, int profile = 1)
    {
        var section = $"[Profile{profile}]";
        var inSection = false;
        foreach (var raw in File.ReadLines(cfgPath))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inSection = string.Equals(line, section, StringComparison.OrdinalIgnoreCase); continue; }
            if (inSection && line.StartsWith("VFCurve=", StringComparison.OrdinalIgnoreCase))
            {
                var hex = line[8..].Trim();
                return hex.Length > 0 ? hex : null;
            }
        }
        return null;
    }

    public static List<Point> Decode(string hex)
    {
        if (hex.Length < 16 || hex.Length % 2 != 0) throw new FormatException("VFCurve: not a hex blob");
        var bytes = Convert.FromHexString(hex);
        var format = BitConverter.ToUInt16(bytes, 2);
        if (format != 2) throw new FormatException($"VFCurve: format {format}, only 2 is known");
        var count = BitConverter.ToInt32(bytes, 4);
        if (count <= 0 || 8 + count * 12 > bytes.Length) throw new FormatException($"VFCurve: {count} points do not fit in {bytes.Length} bytes");
        var points = new List<Point>(count);
        for (var i = 0; i < count; i++)
        {
            var o = 8 + i * 12;
            points.Add(new Point(i, BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4), BitConverter.ToSingle(bytes, o + 8)));
        }
        return points;
    }

    public sealed record Intent(int LockMv, int LockOffsetMhz, int LockMhz, int LockBaseMhz, int LowOffsetMhz);

    /// <summary>
    /// The undervolt the curve expresses: the plateau (the highest target
    /// MHz), the first point that reaches it with its offset and base, and
    /// the offset the low region carries. Null when the curve has no
    /// plateau (it keeps rising: nothing to lock).
    /// </summary>
    public static Intent? IntentOf(IReadOnlyList<Point> points)
    {
        var live = points.Where(p => p.BaseMhz > 0).ToList();
        if (live.Count < 2) return null;
        var plateau = live.Max(p => p.TargetMhz);
        var first = live.First(p => p.TargetMhz >= plateau - 1);
        if (first.Index == live[^1].Index) return null;   // the maximum is the last point: no plateau
        var below = live.Where(p => p.Index < first.Index && p.OffsetMhz != 0).Select(p => p.OffsetMhz).ToList();
        var low = below.Count > 0 ? below.GroupBy(o => o).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key : 0;
        return new Intent((int)Math.Round(first.Mv), (int)Math.Round(first.OffsetMhz), (int)Math.Round(plateau), (int)Math.Round(first.BaseMhz), (int)Math.Round(low));
    }
}

/// <summary>Green Curve's `config.ini`: `[profileN]` or `[controls]` with lock_ci, lock_mhz, gpu_offset_mhz. lock_ci is a curve index, resolved against the live curve.</summary>
public static class GreenCurveIni
{
    public static (int LockIndex, int LockMhz, int GpuOffsetMhz)? Read(string iniPath, int profile = 1)
    {
        string? section = null; int? ci = null, mhz = null, off = null;
        foreach (var raw in File.ReadLines(iniPath))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { section = line.Trim('[', ']').ToLowerInvariant(); continue; }
            if (section != $"profile{profile}" && section != "controls") continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim().ToLowerInvariant(); var value = line[(eq + 1)..].Trim();
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) continue;
            if (key == "lock_ci") ci = v; else if (key == "lock_mhz") mhz = v; else if (key == "gpu_offset_mhz") off = v;
        }
        return ci is { } c && c >= 0 && mhz is > 0 and var m ? (c, m, off ?? 0) : null;
    }
}

/// <summary>Applies a <see cref="GpuProfile"/> to the live curve and verifies it; rolls back to 0 when it does not land.</summary>
public static class CurveApply
{
    public sealed record Result(bool Ok, string Detail, int LockIndex, VfPoint[] Curve);

    public static Result Apply(NvApi api, VfCurve vf, GpuProfile profile, Action<string>? log = null)
    {
        var curve = vf.ReadSettled();
        // The base cannot be derived from a curve that carries offsets: the driver reports the flattened
        // tail at the lock's clock, so clock - offset is fiction there (2655 - (-1000) = 3655 on
        // 2026-09-04, which turned a per-point tail into -1000 everywhere). Green Curve resets
        // before applying and settles 1 s; so does this.
        if (curve.Any(p => p.OffsetKhz != 0))
        {
            log?.Invoke("offsets on the curve: reset to 0 first, then 1 s to settle");
            vf.Reset(log);
            Thread.Sleep(1000);
            curve = vf.ReadSettled();
        }
        if (VfCurve.IndexForMv(curve, profile.LockMv) is not { } lockIndex)
            return new Result(false, $"the curve has no point at {profile.LockMv} mV or above", -1, curve);
        var blackwell = api.Architecture == NvApi.Blackwell || api.Architecture > NvApi.Blackwell;
        var baseMhz = curve[lockIndex].BaseKhz / 1000;
        // A profile from `set --lock MHz@mV` carries a target, not an offset: the offset is derived once, here, against this base.
        var lockOffsetKhz = profile.LockOffsetMhz != 0 ? profile.LockOffsetMhz * 1000 : profile.LockMhz * 1000 - curve[lockIndex].BaseKhz;
        var (targets, mask) = VfCurve.FlattenTargets(curve, lockIndex, lockOffsetKhz, profile.LowOffsetMhz * 1000, blackwell && !profile.TailPerPoint);
        log?.Invoke($"lock at point {lockIndex} ({curve[lockIndex].Mv:F1} mV, base {baseMhz} MHz now): offset {targets[lockIndex] / 1000:+0;-0} MHz -> {(curve[lockIndex].BaseKhz + targets[lockIndex]) / 1000} MHz; {mask.Count(m => m) - 1} other points");
        var left = vf.Apply(targets, mask, log);
        var after = vf.ReadSettled();
        if (VfCurve.DetectLock(after, VfCurve.VerifyToleranceMhz) == lockIndex && after[lockIndex].OffsetKhz == targets[lockIndex])
            return new Result(true, $"flat from {after[lockIndex].Mv:F1} mV at {after[lockIndex].Mhz} MHz now (base {baseMhz} {targets[lockIndex] / 1000:+0;-0}), {left.Count} offsets not exact", lockIndex, after);
        var top = after.Where(p => p.HasData && p.Index <= VfBackend.LastTailPoint).Max(p => p.Mhz);
        log?.Invoke($"not flat from the lock: lock point reads {after[lockIndex].Mhz} MHz (offset {after[lockIndex].OffsetKhz / 1000:+0;-0}), curve tops {top} MHz; rolling back");
        vf.Reset(log);
        return new Result(false, $"the curve did not flatten from the lock (lock point {after[lockIndex].Mhz} MHz, top {top} MHz); offsets reset to 0", lockIndex, after);
    }

    /// <summary>
    /// Is the profile on the curve right now: the lock point carries the
    /// profile's offset. The offset, not the shape nor the MHz: the driver's
    /// base curve sits in two states hundreds of MHz apart, and the shift is
    /// not uniform (233 MHz at 875 mV, 330 at 1240 mV on the reference
    /// machine), so offsets derived in one state leave the curve off flat in
    /// the other while every one of them is still in place; checking the
    /// shape turned each change of state into a "lost" curve and three of
    /// them into a safety lock (2026-09-04 to 2026-09-12). The shape is
    /// verified once, at apply; what can vanish later is the offsets.
    /// </summary>
    public static bool IsApplied(VfPoint[] curve, GpuProfile profile)
        => profile.LockOffsetMhz != 0 && VfCurve.IndexForMv(curve, profile.LockMv) is { } i && curve[i].OffsetKhz == profile.LockOffsetMhz * 1000;

    /// <summary>The clock the lock point reads right now, null when the profile is not on the curve.</summary>
    public static int? LockMhzNow(VfPoint[] curve, GpuProfile profile)
        => IsApplied(curve, profile) && VfCurve.IndexForMv(curve, profile.LockMv) is { } i ? curve[i].Mhz : null;
}
