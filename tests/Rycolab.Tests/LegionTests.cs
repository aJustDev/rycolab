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
}
