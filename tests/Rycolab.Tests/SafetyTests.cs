using Rycolab.Core;

namespace Rycolab.Tests;

public class SearchPlanTests
{
    [Fact]
    public void CoarseMarginsClimbFromTheStartToTheTop()
        => Assert.Equal([-50, -40, -30, -20, -10], Sweep.CoarseMargins(-50, -5, 10));

    [Fact]
    public void CoarseWithTheFineStepIsTheLinearSearch()
        => Assert.Equal(10, Sweep.CoarseMargins(-50, -5, 5).Count());

    [Fact]
    public void FineMarginsFillTheGapBelowTheFirstClean()
    {
        // -50 positive, -40 clean: the only margin in between is -45.
        Assert.Equal([-45], Sweep.FineMargins(-40, -50, 5));
        // First coarse margin already clean: nothing below the start to try.
        Assert.Empty(Sweep.FineMargins(-50, -55, 5));
        // Coarse step of 15 with fine 5: two candidates, tested downwards.
        Assert.Equal([-40, -45], Sweep.FineMargins(-35, -50, 5));
    }
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

    [Theory]
    [InlineData(ZenStates.Core.Cpu.CodeName.Cezanne, -30)]
    [InlineData(ZenStates.Core.Cpu.CodeName.Vermeer, -30)]
    [InlineData(ZenStates.Core.Cpu.CodeName.Rembrandt, -30)]
    [InlineData(ZenStates.Core.Cpu.CodeName.Raphael, -50)]
    [InlineData(ZenStates.Core.Cpu.CodeName.DragonRange, -50)]
    [InlineData(ZenStates.Core.Cpu.CodeName.GraniteRidge, -50)]
    [InlineData(ZenStates.Core.Cpu.CodeName.StrixPoint, -50)]
    public void FloorFollowsTheGeneration(ZenStates.Core.Cpu.CodeName codeName, int floor) => Assert.Equal(floor, Safety.MinMarginFor(codeName));

    [Fact]
    public void FloorIsEnforcedWhenSet()
    {
        var before = Safety.MinMargin;
        try
        {
            Safety.MinMargin = -30;
            Assert.Throws<SafetyViolationException>(() => Safety.ValidateMargin(-31));
            Safety.ValidateMargin(-30);
        }
        finally { Safety.MinMargin = before; }
    }
}

public class CoreMapTests
{
    // Legion Toolkit's EncodeCoreMarginBitmask and ZenStates' MakeCoreMask: ((ccd << 8) | slot) << 20.
    [Theory]
    [InlineData(0, 0x00000000u)]
    [InlineData(3, 0x00300000u)]
    [InlineData(7, 0x00700000u)]
    [InlineData(8, 0x10000000u)]
    [InlineData(11, 0x10300000u)]
    [InlineData(15, 0x10700000u)]
    public void ReferenceMachineMasksMatchLegionToolkit(int core, uint mask)
    {
        // 9955HX3D as the fuses describe it: two CCDs, nothing off, SMT on, 16 cores.
        var map = CoreMap.From(2, 8, [0u, 0u], 2, apu: false, enabledCores: 16);
        Assert.Null(map.Warning);
        Assert.Equal(mask, map.ReadMask(core));
        Assert.Equal(mask, map.WriteMask(core));
        Assert.Equal(mask, CoreMap.Uniform(16).ReadMask(core));
        Assert.Equal(core * 2, map.OsLogical(core));
        Assert.Equal(core / 8, map.Ccd(core));
    }

    [Fact]
    public void ApuReadsFlatIndexAndWritesAtBit20()
    {
        // Ryzen 7 5800H: one CCD of eight, APU.
        var map = CoreMap.From(1, 8, [0u], 2, apu: true, enabledCores: 8);
        Assert.Null(map.Warning);
        Assert.Equal(8, map.Count);
        Assert.Equal(5u, map.ReadMask(5));
        Assert.Equal(0x00500000u, map.WriteMask(5));
        Assert.Equal("1 CCD, 8 cores, SMT on, APU", map.Describe());
    }

    [Fact]
    public void SixCoreCcdSkipsTheFusedSlots()
    {
        // 7600X-like: one CCD, slots 6 and 7 off.
        var map = CoreMap.From(1, 8, [0b1100_0000u], 2, apu: false, enabledCores: 6);
        Assert.Null(map.Warning);
        Assert.Equal(6, map.Count);
        Assert.Equal(0x00500000u, map.WriteMask(5));
        Assert.Equal([(0, 6), (0, 7)], map.DisabledSlots());
        Assert.Equal("1 CCD, 6 cores (off: CCD0 6,7), SMT on", map.Describe());
    }

    [Fact]
    public void TwoSixCoreCcdsUseThePhysicalSlotNotTheIndex()
    {
        // 7900X-like: CCD0 without slots 6,7; CCD1 without slots 0,1. Core 6 is CCD1 slot 2.
        var map = CoreMap.From(2, 8, [0b1100_0000u, 0b0000_0011u], 2, apu: false, enabledCores: 12);
        Assert.Null(map.Warning);
        Assert.Equal(12, map.Count);
        Assert.Equal(1, map.Ccd(6));
        Assert.Equal(2, map.Physical(6));
        Assert.Equal(0x10200000u, map.WriteMask(6));
        Assert.Equal(12, map.OsLogical(6));
        Assert.Equal(0x10700000u, map.WriteMask(11));
        Assert.Equal("2 CCDs, 6+6 cores (off: CCD0 6,7; CCD1 0,1), SMT on", map.Describe());
        Assert.Equal(6, map.CoresOfCcd(1).Count());
    }

    [Fact]
    public void SmtOffPutsCoreNOnLogicalN()
    {
        var map = CoreMap.From(2, 8, [0u, 0u], 1, apu: false, enabledCores: 16);
        Assert.Equal(9, map.OsLogical(9));
        Assert.Contains("SMT off", map.Describe());
    }

    [Fact]
    public void FusesAndCpuidDisagreeIsNotTrusted()
    {
        var map = CoreMap.From(2, 8, [0u, 0u], 2, apu: false, enabledCores: 12);
        Assert.NotNull(map.Warning);
        Assert.Contains("16 cores", map.Warning);
        Assert.Equal(12, map.Count);   // the uniform fallback, for reading only
    }

    [Fact]
    public void UnreadableTopologyFallsBackToUniformWithAWarning()
    {
        var map = CoreMap.From(0, 0, [], 2, apu: false, enabledCores: 8);
        Assert.NotNull(map.Warning);
        Assert.Equal(8, map.Count);
        Assert.Equal(0x00300000u, map.WriteMask(3));
    }

    [Fact]
    public void IndicesPastTheMapFallBackToTheUniformLayout()
    {
        var map = CoreMap.From(1, 8, [0u], 2, apu: true, enabledCores: 8);
        Assert.Equal(1, map.Ccd(9));
        Assert.Equal(18, map.OsLogical(9));
    }

    [Fact]
    public void TopologyHelpersFollowTheDefaultMap()
    {
        Assert.Equal(6, Topology.LogicalProcessor(3));
        Assert.Equal(1L << 6, Topology.AffinityMask(3));
        Assert.Equal("CCD0", Topology.CcdName(7));
        Assert.Equal("CCD1", Topology.CcdName(8));
        Assert.Equal("CCD1 (Tdie)", Topology.CcdTempSensor(0));   // LHM numbers from 1
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
