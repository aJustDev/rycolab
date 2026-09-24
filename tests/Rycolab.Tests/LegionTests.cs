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
}
