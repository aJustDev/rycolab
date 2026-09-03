namespace Rycolab.Core.Gpu;

/// <summary>One point of the V/F curve: the clock the driver reports at that voltage, and the offset we put on it.</summary>
public sealed record VfPoint(int Index, int Khz, int Uv, int OffsetKhz)
{
    public bool HasData => Khz != 0;
    public int Mhz => (Khz + 500) / 1000;
    public double Mv => Uv / 1000.0;
    /// <summary>The clock the driver would report with no offset.</summary>
    public int BaseKhz => Khz - OffsetKhz;
}

/// <summary>
/// The V/F curve of the first NVIDIA GPU: read it, put per-point frequency
/// offsets on it, verify, reset. Mechanics ported from Green Curve
/// (gpu_backend.cpp, main_runtime_gpu.cpp, gpu_backend_apply.cpp; Copyright
/// (c) 2026 aufkrawall, MIT): the editable-point mask from the info call, a
/// batched SetControl with a per-point fallback, readback until two reads
/// agree, and the Blackwell way of flattening the tail (the driver ignores
/// individual tail deltas, so every tail point gets the driver's minimum
/// offset and the lock point alone sets the ceiling).
/// </summary>
public sealed class VfCurve
{
    public const int DefaultMinOffsetKhz = -1000000, DefaultMaxOffsetKhz = 1000000;
    public const int VerifyToleranceMhz = 8;

    private readonly NvApi _api;
    private readonly byte[] _mask = new byte[32];
    private int _numClocks;

    public VfBackend Backend { get; }
    public int EditablePoints { get; }

    public VfCurve(NvApi api)
    {
        _api = api;
        Backend = VfBackend.For(api.Architecture);
        _numClocks = Backend.DefaultNumClocks;
        // The editable-point mask; every bit set when the call is not there (the driver then decides).
        Array.Fill(_mask, (byte)0xFF, 0, 16);
        using var info = new NvApi.Buffer(Backend.InfoSize, Backend.InfoVersion);
        info.Fill(Backend.InfoMaskOffset, 32, 0xFF);
        if (_api.Call(Backend.GetInfoId, info) == 0)
        {
            Array.Copy(info.ReadBytes(Backend.InfoMaskOffset, 32), _mask, 32);
            var n = info.ReadInt32(Backend.InfoNumClocksOffset);
            if (n > 0) _numClocks = n;
        }
        EditablePoints = Enumerable.Range(0, VfBackend.Points).Count(IsEditable);
    }

    public bool IsEditable(int point) => (_mask[point / 8] & (1 << (point % 8))) != 0;

    /// <summary>The 128 points with their offsets; throws when the driver refuses the read.</summary>
    public VfPoint[] Read()
    {
        using var status = new NvApi.Buffer(Backend.StatusSize, Backend.StatusVersion);
        status.WriteBytes(Backend.StatusMaskOffset, _mask);
        status.WriteInt32(Backend.StatusNumClocksOffset, _numClocks);
        var rc = _api.Call(Backend.GetStatusId, status);
        if (rc != 0) throw new InvalidOperationException($"V/F curve read failed (NvAPI status {rc})");
        using var control = ReadControl();
        var points = new VfPoint[VfBackend.Points];
        for (var i = 0; i < points.Length; i++)
            points[i] = new VfPoint(i, status.ReadInt32(Backend.StatusEntry(i)), status.ReadInt32(Backend.StatusEntry(i) + 4), control.ReadInt32(Backend.ControlDelta(i)));
        return points;
    }

    /// <summary>Reads until two consecutive reads agree (the driver takes a moment after a write).</summary>
    public VfPoint[] ReadSettled(int attempts = 6, int delayMs = 25)
    {
        var last = Read();
        for (var i = 0; i < attempts; i++)
        {
            Thread.Sleep(delayMs);
            var now = Read();
            if (now.SequenceEqual(last)) return now;
            last = now;
        }
        return last;
    }

    private NvApi.Buffer ReadControl()
    {
        var control = new NvApi.Buffer(Backend.ControlSize, Backend.ControlVersion);
        control.WriteBytes(Backend.ControlMaskOffset, _mask);
        var rc = _api.Call(Backend.GetControlId, control);
        if (rc != 0) { control.Dispose(); throw new InvalidOperationException($"V/F control read failed (NvAPI status {rc})"); }
        return control;
    }

    /// <summary>One SetControl with every masked point's delta; the NvAPI status (0 ok).</summary>
    public int SetOffsets(int[] targetsKhz, bool[] mask)
    {
        using var control = ReadControl();
        control.Fill(Backend.ControlMaskOffset, 32, 0);
        var any = false;
        for (var i = 0; i < VfBackend.Points; i++)
        {
            if (!mask[i] || !IsEditable(i)) continue;
            control.WriteByte(Backend.ControlMaskOffset + i / 8, (byte)(control.ReadByte(Backend.ControlMaskOffset + i / 8) | (1 << (i % 8))));
            control.WriteInt32(Backend.ControlDelta(i), targetsKhz[i]);
            any = true;
        }
        return any ? _api.Call(Backend.SetControlId, control) : 0;
    }

    public int SetOffset(int point, int khz)
    {
        var targets = new int[VfBackend.Points]; var mask = new bool[VfBackend.Points];
        targets[point] = khz; mask[point] = true;
        return SetOffsets(targets, mask);
    }

    /// <summary>
    /// Writes the masked offsets and reads them back: a batch, then a
    /// per-point retry for whatever did not land. A point the driver pins
    /// at 0 when asked for something else is accepted as not offsettable
    /// (Green Curve: a placeholder entry). Returns the offsets still off
    /// target, empty when everything landed.
    /// </summary>
    public List<(int Point, int Wanted, int Got)> Apply(int[] targetsKhz, bool[] mask, Action<string>? log = null)
    {
        var rc = SetOffsets(targetsKhz, mask);
        log?.Invoke($"batch write: status {rc}");
        var got = ReadSettled();
        var pending = Enumerable.Range(0, VfBackend.Points).Where(i => mask[i] && IsEditable(i) && got[i].HasData && got[i].OffsetKhz != targetsKhz[i]).ToList();
        foreach (var i in pending)
        {
            if (targetsKhz[i] != 0 && got[i].OffsetKhz == 0) { log?.Invoke($"point {i}: the driver keeps it at 0, not offsettable"); continue; }
            rc = SetOffset(i, targetsKhz[i]);
            log?.Invoke($"point {i}: retried alone, status {rc}");
        }
        got = ReadSettled();
        return Enumerable.Range(0, VfBackend.Points)
            .Where(i => mask[i] && IsEditable(i) && got[i].HasData && got[i].OffsetKhz != targetsKhz[i] && !(targetsKhz[i] != 0 && got[i].OffsetKhz == 0))
            .Select(i => (i, targetsKhz[i], got[i].OffsetKhz)).ToList();
    }

    /// <summary>Every offset back to 0. True when the readback shows none left.</summary>
    public bool Reset(Action<string>? log = null)
    {
        var now = Read();
        var mask = now.Select(p => p.OffsetKhz != 0).ToArray();
        if (!mask.Any(m => m)) return true;
        var left = Apply(new int[VfBackend.Points], mask, log);
        return left.Count == 0;
    }

    // ---- the arithmetic, without hardware ----

    /// <summary>The first point with data at or above the voltage; null when the curve never gets there.</summary>
    public static int? IndexForMv(VfPoint[] curve, double mv)
    {
        for (var i = 0; i <= VfBackend.LastTailPoint; i++)
            if (curve[i].HasData && curve[i].Mv >= mv) return i;
        return null;
    }

    /// <summary>
    /// The offsets that flatten the curve from the lock point on: the lock
    /// point gets <paramref name="lockOffsetKhz"/> (a static offset, the way
    /// Afterburner stores a curve: the clock it yields rides the driver's
    /// base); below the lock a uniform offset, capped so no point rises
    /// above the lock; the tail gets the driver's minimum offset on
    /// Blackwell (per-point deltas there are ignored) or lock - base
    /// elsewhere. Point 127 (low power) is left alone.
    /// </summary>
    public static (int[] Targets, bool[] Mask) FlattenTargets(VfPoint[] curve, int lockIndex, int lockOffsetKhz, int lowOffsetKhz, bool blackwell,
        int minKhz = DefaultMinOffsetKhz, int maxKhz = DefaultMaxOffsetKhz)
    {
        var targets = new int[VfBackend.Points]; var mask = new bool[VfBackend.Points];
        var lockKhz = curve[lockIndex].BaseKhz + lockOffsetKhz;
        for (var i = 0; i <= VfBackend.LastTailPoint; i++)
        {
            if (!curve[i].HasData) continue;
            var toLock = lockKhz - curve[i].BaseKhz;
            targets[i] = i < lockIndex ? Math.Min(lowOffsetKhz, toLock) : i == lockIndex ? toLock : blackwell ? minKhz : toLock;
            targets[i] = Math.Clamp(targets[i], minKhz, maxKhz);
            mask[i] = true;
        }
        return (targets, mask);
    }

    /// <summary>The curve as read after a flatten: the lock point at its MHz and nothing above it, within the tolerance.</summary>
    public static bool IsFlatAt(VfPoint[] curve, int lockIndex, int lockMhz, int toleranceMhz = VerifyToleranceMhz)
    {
        if (!curve[lockIndex].HasData || Math.Abs(curve[lockIndex].Mhz - lockMhz) > toleranceMhz) return false;
        for (var i = 0; i <= VfBackend.LastTailPoint; i++)
            if (curve[i].HasData && curve[i].Mhz > lockMhz + toleranceMhz) return false;
        return true;
    }

    /// <summary>Where the curve stops rising: the first point whose MHz every later point stays within the tolerance of. Null when it keeps rising.</summary>
    public static int? DetectLock(VfPoint[] curve, int toleranceMhz = 1)
    {
        var data = Enumerable.Range(0, VfBackend.LastTailPoint + 1).Where(i => curve[i].HasData).ToList();
        for (var k = 0; k < data.Count - 1; k++)
        {
            var i = data[k];
            var mhz = curve[i].Mhz;
            if (data.Skip(k + 1).All(j => Math.Abs(curve[j].Mhz - mhz) <= toleranceMhz)) return i;
        }
        return null;
    }
}
