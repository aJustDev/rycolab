using Rycolab.Core;

namespace Rycolab.Tests;

public class UserIdleTests
{
    [Fact]
    public void IdleIsNullOrWithinUptime()
    {
        var s = UserIdle.Seconds();
        Assert.True(s is null || (s >= 0 && s * 1000L <= Environment.TickCount64));
    }
}

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
