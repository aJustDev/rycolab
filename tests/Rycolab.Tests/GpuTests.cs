using Rycolab.Core.Gpu;

namespace Rycolab.Tests;

public class VfCurveTests
{
    /// <summary>A curve shaped like the 5080's: 180 MHz up to 640 mV, then rising to 2827 MHz at 1240 mV; point 127 is the low-power point.</summary>
    private static VfPoint[] Curve(Func<int, int>? offset = null)
    {
        var pts = new VfPoint[VfBackend.Points];
        for (var i = 0; i < 127; i++)
        {
            var uv = 450000 + i * 6250;
            var khz = i < 30 ? 180000 : 180000 + (i - 30) * 27000;
            var off = offset?.Invoke(i) ?? 0;
            pts[i] = new VfPoint(i, khz + off, uv, off);
        }
        pts[127] = new VfPoint(127, 405000, 525000, 0);
        return pts;
    }

    [Fact]
    public void PointArithmetic()
    {
        var p = new VfPoint(70, 2159600, 890000, -15000);
        Assert.Equal(2160, p.Mhz);
        Assert.Equal(890.0, p.Mv);
        Assert.Equal(2174600, p.BaseKhz);
        Assert.False(new VfPoint(3, 0, 0, 0).HasData);
    }

    [Fact]
    public void IndexForVoltage()
    {
        var c = Curve();
        Assert.Equal(0, VfCurve.IndexForMv(c, 400));
        Assert.Equal(72, VfCurve.IndexForMv(c, 900));   // 450 + 72 * 6.25 = 900
        Assert.Null(VfCurve.IndexForMv(c, 1300));
    }

    [Fact]
    public void FlattenOnBlackwellFloorsTheTailAndCapsBelowTheLock()
    {
        var c = Curve();
        var lockIndex = 80;   // 950 mV, 1530 MHz on this synthetic curve
        var (t, m) = VfCurve.FlattenTargets(c, lockIndex, lockMhz: 1600, lowOffsetKhz: 300000, blackwell: true);
        Assert.Equal(1600000 - c[80].BaseKhz, t[80]);
        Assert.True(m[80]);
        Assert.Equal(-1000000, t[81]);
        Assert.Equal(-1000000, t[126]);
        Assert.False(m[127]);   // the low-power point is left alone
        Assert.Equal(300000, t[10]);   // far below the lock: the full low offset
        // Just below the lock the low offset would overshoot the lock: capped at lock - base.
        Assert.Equal(1600000 - c[79].BaseKhz, t[79]);
        Assert.True(t[79] < 300000);
    }

    [Fact]
    public void FlattenElsewhereUsesPerPointDeltasForTheTail()
    {
        var c = Curve();
        var (t, _) = VfCurve.FlattenTargets(c, 80, 1600, 0, blackwell: false);
        Assert.Equal(1600000 - c[100].BaseKhz, t[100]);
        Assert.Equal(1600000 - c[110].BaseKhz, t[110]);
        Assert.Equal(-1000000, t[126]);   // 1600 - 2772 MHz would exceed the driver range: clamped
    }

    [Fact]
    public void FlattenClampsToTheDriverRange()
    {
        var c = Curve();
        var (t, _) = VfCurve.FlattenTargets(c, 80, 4000, 0, blackwell: true, minKhz: -500000, maxKhz: 500000);
        Assert.Equal(500000, t[80]);
        Assert.Equal(-500000, t[100]);
    }

    [Fact]
    public void FlatDetectionAndVerification()
    {
        var rising = Curve();
        Assert.Null(VfCurve.DetectLock(rising));
        Assert.False(VfCurve.IsFlatAt(rising, 80, rising[80].Mhz));

        // After a Blackwell flatten the lock point reads at its MHz and the floored tail under it
        // (the floor of -1000 MHz only works for a lock near the top, as an undervolt lock is).
        var flat = Curve(i => i > 110 ? -1000000 : i == 110 ? 60000 : 0);
        Assert.True(VfCurve.IsFlatAt(flat, 110, flat[110].Mhz));
        Assert.False(VfCurve.IsFlatAt(flat, 110, flat[110].Mhz + 20));
        var tooLow = Curve(i => i > 80 ? -1000000 : 0);
        Assert.False(VfCurve.IsFlatAt(tooLow, 80, tooLow[80].Mhz));   // 2772 - 1000 still tops 1530

        // A literally flat tail (the driver clamped it) is detected at its first point.
        var clamped = Curve(i => i > 80 ? (1530000 - (180000 + (i - 30) * 27000)) : 0);
        Assert.Equal(80, VfCurve.DetectLock(clamped));
    }

    [Fact]
    public void BackendTable()
    {
        Assert.Equal("blackwell", VfBackend.For(NvApi.Blackwell).Name);
        Assert.False(VfBackend.For(NvApi.Lovelace).BestGuessOnly);
        Assert.True(VfBackend.For(0x1C0).BestGuessOnly);
        var b = VfBackend.For(NvApi.Blackwell);
        Assert.Equal(0x48 + 70 * 0x1C, b.StatusEntry(70));
        Assert.Equal(0x44 + 70 * 0x24 + 0x14, b.ControlDelta(70));
        Assert.True(b.ControlDelta(127) + 4 <= b.ControlSize);
        Assert.True(b.StatusEntry(127) + 8 <= b.StatusSize);
    }
}

public class AfterburnerTests
{
    /// <summary>A format-2 blob like the reference machine's: +300 MHz low, tapering to a plateau of 2655 MHz from 900 mV.</summary>
    private static string Blob()
    {
        var bytes = new List<byte> { 0, 0, 2, 0 };
        bytes.AddRange(BitConverter.GetBytes(127));
        for (var i = 0; i < 127; i++)
        {
            var mv = 450 + i * 6.25;
            var baseMhz = i < 30 ? 180.0 : 180 + (i - 30) * 30.5;
            var offset = Math.Min(300.0, 2655 - baseMhz);
            foreach (var f in new[] { (float)offset, (float)mv, (float)baseMhz }) bytes.AddRange(BitConverter.GetBytes(f));
        }
        return Convert.ToHexString(bytes.ToArray());
    }

    [Fact]
    public void DecodesTriplets()
    {
        var pts = Afterburner.Decode(Blob());
        Assert.Equal(127, pts.Count);
        Assert.Equal(300, pts[0].OffsetMhz);
        Assert.Equal(450, pts[0].Mv);
        Assert.Equal(180, pts[0].BaseMhz);
        Assert.Equal(480, pts[0].TargetMhz);
        Assert.Equal(2655, pts[126].TargetMhz, 1);
    }

    [Fact]
    public void IntentIsThePlateauItsFirstVoltageAndTheLowOffset()
    {
        var intent = Afterburner.Intent(Afterburner.Decode(Blob()))!.Value;
        Assert.Equal(2655, intent.LockMhz);
        Assert.Equal(300, intent.LowOffsetMhz);
        // base first reaches 2355 (2655 - 300) at i = 102: 450 + 102 * 6.25 = 1087.5 mV, rounded to even
        Assert.Equal(1088, intent.LockMv);
    }

    [Fact]
    public void ARisingCurveHasNoIntent()
    {
        var bytes = new List<byte> { 0, 0, 2, 0 };
        bytes.AddRange(BitConverter.GetBytes(3));
        foreach (var (o, mv, b) in new[] { (0f, 700f, 1000f), (0f, 800f, 1500f), (0f, 900f, 2000f) })
            foreach (var f in new[] { o, mv, b }) bytes.AddRange(BitConverter.GetBytes(f));
        Assert.Null(Afterburner.Intent(Afterburner.Decode(Convert.ToHexString(bytes.ToArray()))));
    }

    [Fact]
    public void RejectsOtherFormats()
    {
        Assert.Throws<FormatException>(() => Afterburner.Decode("000003000100000000000000000000000000000000000000"));
        Assert.Throws<FormatException>(() => Afterburner.Decode("00"));
    }

    [Fact]
    public void ReadsTheProfileSection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rycolab-test-{Guid.NewGuid():N}.cfg");
        File.WriteAllText(path, "[Startup]\r\nFormat=2\r\nVFCurve=\r\n[Profile1]\r\nFormat=2\r\nCoreClkBoost=0\r\nVFCurve=" + Blob() + "\r\n[Profile2]\r\nVFCurve=\r\n");
        try
        {
            Assert.NotNull(Afterburner.ReadVfCurve(path, 1));
            Assert.Null(Afterburner.ReadVfCurve(path, 2));
            Assert.Null(Afterburner.ReadVfCurve(path, 3));
        }
        finally { File.Delete(path); }
    }
}

public class GreenCurveIniTests
{
    [Fact]
    public void ReadsLockAndOffset()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rycolab-test-{Guid.NewGuid():N}.ini");
        File.WriteAllText(path, "[profiles]\r\nselected=1\r\n[profile1]\r\ngpu_offset_mhz=150\r\nlock_ci=88\r\nlock_mhz=2610\r\nlock_mode=1\r\n[profile2]\r\nlock_ci=-1\r\nlock_mhz=0\r\n");
        try
        {
            var p = GreenCurveIni.Read(path, 1)!.Value;
            Assert.Equal((88, 2610, 150), p);
            Assert.Null(GreenCurveIni.Read(path, 2));
        }
        finally { File.Delete(path); }
    }
}

public class GpuProfileTests
{
    [Fact]
    public void AppliedMeansFlatAtTheLock()
    {
        var pts = new VfPoint[VfBackend.Points];
        for (var i = 0; i < 127; i++) pts[i] = new VfPoint(i, Math.Min(2400000, 1000000 + i * 20000), 450000 + i * 6250, 0);
        pts[127] = new VfPoint(127, 405000, 525000, 0);
        var p = new GpuProfile { LockMv = 887, LockMhz = 2400 };   // point 70 = 887.5 mV = 2400 MHz, flat after
        Assert.True(CurveApply.IsApplied(pts, p));
        Assert.False(CurveApply.IsApplied(pts, new GpuProfile { LockMv = 887, LockMhz = 2300 }));
        Assert.False(CurveApply.IsApplied(pts, new GpuProfile { LockMv = 1300, LockMhz = 2400 }));
        Assert.Equal("2400 MHz from 887 mV", p.Describe);
    }
}
