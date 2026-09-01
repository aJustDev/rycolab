using Rycolab.Core;

namespace Rycolab.Tests;

public class CpuLoadTests
{
    [Fact]
    public void FirstCallPrimesThenAPercentage()
    {
        var load = new CpuLoad();
        Assert.Null(load.Percent());
        Thread.Sleep(250);
        var p = load.Percent();
        Assert.NotNull(p);
        Assert.InRange(p!.Value, 0, 100);
    }
}
