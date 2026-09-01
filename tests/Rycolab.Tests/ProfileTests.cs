using Rycolab.Core;

namespace Rycolab.Tests;

public class ProfileTests
{
    private static readonly CpuFingerprint Here = new() { CpuName = "AMD Ryzen 9 9955HX3D 16-Core Processor", Cores = 16, SmuType = "TYPE_CPU4" };

    private static Profile Measured()
    {
        var limits = Enumerable.Range(0, Topology.MaxCores).ToDictionary(c => c, c => (int?)-45);
        return Profile.FromLimits(limits, new Plan { Base = -5, SafetyMargin = 5 }, "camp", Here);
    }

    [Fact]
    public void NoSourceIsRefused()
    {
        var p = new Profile { Fingerprint = Here };
        Assert.Contains("no source", p.RefusalReason(Here));
    }

    [Fact]
    public void NoFingerprintIsRefused()
    {
        var p = Measured();
        p.Fingerprint = null;
        Assert.Contains("fingerprint", p.RefusalReason(Here));
    }

    [Fact]
    public void AnotherCpuIsRefused()
    {
        var other = new CpuFingerprint { CpuName = "AMD Ryzen 7 5800H with Radeon Graphics", Cores = 8, SmuType = "TYPE_APU1" };
        Assert.Contains("another CPU", Measured().RefusalReason(other));
    }

    [Fact]
    public void BelowMeasuredLimitIsRefused()
    {
        var p = Measured();
        p.Cores[3] = -46;
        Assert.Contains("core 3", p.RefusalReason(Here));
    }

    [Fact]
    public void MeasuredProfileIsAccepted() => Assert.Null(Measured().RefusalReason(Here));

    [Fact]
    public void FromLimitsAddsTheMarginCapsAtTopAndKeepsBaselineWithoutLimit()
    {
        var limits = new Dictionary<int, int?> { [0] = -45, [1] = -5, [2] = null };
        var p = Profile.FromLimits(limits, new Plan { Base = -5, Top = -5, SafetyMargin = 5 }, "camp", Here);
        Assert.Equal(-40, p.Cores[0]);
        Assert.Equal(-5, p.Cores[1]);    // -5 + 5 = 0 would raise the voltage: capped at top
        Assert.Equal(-5, p.Cores[2]);    // no limit: baseline
        Assert.Equal(-5, p.Cores[15]);   // never swept: baseline
        Assert.Equal(-45, p.Source!.Limits[0]);
        Assert.Null(p.Source.Limits[2]);
        Assert.Equal(5, p.Source.SafetyMargin);
    }

    [Fact]
    public void FromLimitsHonoursACustomMargin()
    {
        var p = Profile.FromLimits(new Dictionary<int, int?> { [0] = -45 }, new Plan(), "camp", Here, margin: 10);
        Assert.Equal(-35, p.Cores[0]);
    }

    [Fact]
    public void MismatchesListsCoresOffProfile()
    {
        var p = Measured();
        var readings = Enumerable.Range(0, 16).Select(i => new CoreReading(i, Topology.CcdName(i), 0, i == 4 ? -5 : -40)).ToList();
        Assert.Equal([4], p.Mismatches(readings));
    }

    [Fact]
    public void WrongLengthIsRejected()
    {
        var p = new Profile { Cores = [-5, -5] };
        Assert.Throws<SafetyViolationException>(p.Validate);
    }

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rycolab-test-{Guid.NewGuid():N}.json");
        try
        {
            var p = Measured();
            p.Save(path);
            var back = Profile.Load(path);
            Assert.Equal(p.Cores, back.Cores);
            Assert.Equal("camp", back.Source!.Campaign);
            Assert.True(back.Fingerprint!.Matches(Here));
        }
        finally { File.Delete(path); }
    }
}
