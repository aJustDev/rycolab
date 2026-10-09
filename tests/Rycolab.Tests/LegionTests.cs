using Rycolab.Core.Legion;

namespace Rycolab.Tests;

public class LenovoEcModeTests
{
    [Fact]
    public void ModeNamesRoundTrip()
    {
        foreach (var name in new[] { "quiet", "balanced", "performance", "extreme", "custom" })
            Assert.Equal(name, LenovoEc.ModeName(LenovoEc.ModeFromName(name)));
        Assert.Equal(224, LenovoEc.ModeFromName("Extreme"));
        Assert.Equal(LenovoEc.QuietMode, LenovoEc.ModeFromName("quiet"));
        Assert.Null(LenovoEc.ModeFromName("turbo"));
        Assert.Null(LenovoEc.ModeFromName("0"));   // SetSmartFanMode(0) is invalid and ignored by the EC
    }

    [Fact]
    public void DgpuArrivalIsThereWithTheCardAndHoldsStill()
    {
        // On a machine without the card (CI) both are empty; with it, the stamp is what tells the guard its NVML handle is still good.
        var arrival = LenovoEc.DgpuArrival();
        if (LenovoEc.DgpuPresent()) Assert.False(string.IsNullOrWhiteSpace(arrival));
        Assert.Equal(arrival, LenovoEc.DgpuArrival());
    }

    [Fact]
    public void TheCardCameBackWhenBothStampsWereReadAndDiffer()
    {
        // 2026-10-02: the line dropped for 20 s, the card left and came back between two ticks and both saw it present.
        Assert.True(LenovoEc.DgpuCameBack("arrival-1 install-1", "arrival-2 install-1"));
        // A new driver moves the install date.
        Assert.True(LenovoEc.DgpuCameBack("arrival-1 install-1", "arrival-1 install-2"));
        Assert.False(LenovoEc.DgpuCameBack("arrival-1 install-1", "arrival-1 install-1"));
        // A stamp that could not be read (WMI failed, the first tick) is not a return.
        Assert.False(LenovoEc.DgpuCameBack(null, "arrival-1 install-1"));
        Assert.False(LenovoEc.DgpuCameBack("arrival-1 install-1", null));
        Assert.False(LenovoEc.DgpuCameBack(null, null));
    }
}
