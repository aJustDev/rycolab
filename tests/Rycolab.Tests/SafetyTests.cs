using Rycolab.Core;

namespace Rycolab.Tests;

public class StepperTests
{
    [Fact]
    public void PathDownWalksInStopsOfAtMostThree()
    {
        var path = Stepper.BuildPath(-5, -50);
        Assert.Equal(15, path.Length);
        Assert.Equal(-50, path[^1]);
        var prev = -5;
        foreach (var stop in path)
        {
            Assert.InRange(prev - stop, 1, Safety.MaxStepBetweenLevels);
            prev = stop;
        }
    }

    [Fact]
    public void PathUpEndsAtTarget()
    {
        var path = Stepper.BuildPath(-50, -5);
        Assert.Equal(-5, path[^1]);
        Assert.All(path.Zip(path.Skip(1)), p => Assert.InRange(p.Second - p.First, 1, Safety.MaxStepBetweenLevels));
    }

    [Theory]
    [InlineData(-5, -5, new int[0])]
    [InlineData(-5, -7, new[] { -7 })]
    [InlineData(-5, -9, new[] { -8, -9 })]
    [InlineData(-10, -6, new[] { -7, -6 })]
    public void SmallPaths(int from, int to, int[] expected) => Assert.Equal(expected, Stepper.BuildPath(from, to));
}

public class SafetyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(-51)]
    public void OutOfRangeMarginIsRejected(int margin) => Assert.Throws<SafetyViolationException>(() => Safety.ValidateMargin(margin));

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    [InlineData(-25)]
    public void InRangeMarginPasses(int margin) => Safety.ValidateMargin(margin);

    [Fact]
    public void StepAboveLimitIsRejected() => Assert.Throws<SafetyViolationException>(() => Safety.ValidateStep(-5, -9));
}

public class TopologyTests
{
    // Legion Toolkit's EncodeCoreMarginBitmask: ((ccd << 8) | core) << 20.
    [Theory]
    [InlineData(0, 0x00000000u)]
    [InlineData(3, 0x00300000u)]
    [InlineData(7, 0x00700000u)]
    [InlineData(8, 0x10000000u)]
    [InlineData(11, 0x10300000u)]
    [InlineData(15, 0x10700000u)]
    public void CcdMaskMatchesLegionToolkit(int core, uint mask)
    {
        Assert.Equal(mask, Topology.CcdMask(core));
        Assert.Equal(mask, Topology.ReadMask(apu: false, core));
        Assert.Equal(mask, Topology.WriteMask(apu: false, core));
    }

    [Fact]
    public void ApuReadsFlatIndexAndWritesAtBit20()
    {
        Assert.Equal(5u, Topology.ReadMask(apu: true, 5));
        Assert.Equal(0x00500000u, Topology.WriteMask(apu: true, 5));
    }

    [Fact]
    public void LogicalProcessorAndNames()
    {
        Assert.Equal(6, Topology.LogicalProcessor(3));
        Assert.Equal(1L << 6, Topology.AffinityMask(3));
        Assert.Equal("CCD0", Topology.CcdName(7));
        Assert.Equal("CCD1", Topology.CcdName(8));
        Assert.Equal("CCD1 (Tdie)", Topology.CcdTempSensor(0));   // LHM numbers from 1
        Assert.Equal(8, Topology.FirstCoreOfCcd(1));
    }
}

public class WheaTests
{
    [Fact]
    public void XPathNamesProvidersIdsAndUtcTime()
    {
        var since = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var xpath = Whea.XPath(since, ("Microsoft-Windows-WHEA-Logger", [17, 18]), ("Microsoft-Windows-Kernel-Power", [41]));
        Assert.Equal(
            "*[System[((Provider[@Name='Microsoft-Windows-WHEA-Logger'] and (EventID=17 or EventID=18)) or " +
            "(Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=41))) and TimeCreated[@SystemTime>='2026-09-01T12:00:00.0000000Z']]]",
            xpath);
    }

    [Fact]
    public void PcieEventsDoNotCount()
    {
        var (counts, apic) = Whea.Interpret("Microsoft-Windows-WHEA-Logger", 17, "<Event><EventData><Data Name=\"ErrorSource\">4</Data></EventData></Event>");
        Assert.False(counts);
        Assert.Null(apic);
    }

    [Theory]
    [InlineData(18, "<Data Name=\"ApicId\">22</Data>", 22)]
    [InlineData(19, "<Data Name=\"ApicId\">0x16</Data>", 22)]
    [InlineData(47, "<Data Name=\"MemoryComponent\">1</Data>", null)]
    public void MachineCheckAndMemoryCount(int id, string xml, int? apic)
    {
        var r = Whea.Interpret("Microsoft-Windows-WHEA-Logger", id, xml);
        Assert.True(r.Counts);
        Assert.Equal(apic, r.ApicId);
    }

    [Fact]
    public void KernelPowerAlwaysCounts() => Assert.True(Whea.Interpret("Microsoft-Windows-Kernel-Power", 41, "").Counts);
}

public class ValidationTests
{
    private static Validation V(long guardedHours, int daysAgo, int whea = 0, int resets = 0) => new()
    {
        StartedAt = DateTime.Now.AddDays(-daysAgo), GuardedSeconds = guardedHours * 3600, Whea = whea, Resets = resets,
    };

    [Fact] public void TwentyGuardedHoursIsSteady() => Assert.True(V(20, 1).IsSteady);
    [Fact] public void SevenDaysWithEightHoursIsSteady() => Assert.True(V(8, 7).IsSteady);
    [Fact] public void SevenDaysBarelyGuardedIsNot() => Assert.False(V(1, 7).IsSteady);
    [Fact] public void NineteenHoursInTwoDaysIsNot() => Assert.False(V(19, 2).IsSteady);
    [Fact] public void AnyWheaBlocks() => Assert.False(V(40, 10, whea: 1).IsSteady);
    [Fact] public void AnyResetBlocks() => Assert.False(V(40, 10, resets: 1).IsSteady);
}

public class SweepBootTests
{
    [Fact]
    public void SameBootWithinTolerance()
    {
        var boot = new DateTime(2026, 9, 1, 8, 0, 0);
        Assert.True(Sweep.SameBoot(boot, boot.AddSeconds(30)));
        Assert.True(Sweep.SameBoot(boot, boot.AddSeconds(-30)));
    }

    [Fact]
    public void RebootIsNotTheSameBoot() => Assert.False(Sweep.SameBoot(new DateTime(2026, 9, 1, 8, 0, 0), new DateTime(2026, 9, 1, 8, 10, 0)));

    [Fact]
    public void OldFileWithoutBootIsAHang() => Assert.False(Sweep.SameBoot(null, DateTime.Now));

    [Fact]
    public void BootTimeIsInThePast() => Assert.True(Sweep.BootTime() < DateTime.Now);
}
