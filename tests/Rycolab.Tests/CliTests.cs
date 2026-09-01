using Rycolab.Cli;
using Rycolab.Cli.Commands;

namespace Rycolab.Tests;

public class ArgsTests
{
    [Fact]
    public void OptionsWithEqualsSpaceAndBare()
    {
        var a = new Args(["campaign1", "--core=3", "--margin", "-40", "--plain"]);
        Assert.Equal("3", a.Get("core"));
        Assert.Equal(-40, a.GetInt("margin"));
        Assert.True(a.Has("plain"));
        Assert.Null(a.Get("plain"));
        Assert.Equal(["campaign1"], a.Positional);
    }

    /// <summary>A bare option swallows the next token: positionals go before the options (every command does).</summary>
    [Fact]
    public void BareOptionTakesTheNextTokenAsItsValue()
    {
        var a = new Args(["--plain", "campaign1"]);
        Assert.Equal("campaign1", a.Get("plain"));
        Assert.Empty(a.Positional);
    }

    [Fact]
    public void NamesAreCaseInsensitive() => Assert.True(new Args(["--Quick"]).Has("quick"));

    [Fact]
    public void TrailingFlagIsRecorded()
    {
        var a = new Args(["show", "--force"]);
        Assert.True(a.Has("force"));
        Assert.Equal(["show"], a.Positional);
    }

    [Fact]
    public void NonNumericValueIsNotAnInt()
    {
        var a = new Args(["--brightness", "keep"]);
        Assert.True(a.Has("brightness"));
        Assert.Null(a.GetInt("brightness"));
        Assert.Equal("keep", a.Get("brightness"));
    }

    [Fact]
    public void FallbackWhenMissing() => Assert.Equal("0", new Args([]).Get("core", "0"));
}

public class ParseCoresTests
{
    [Fact] public void Range() => Assert.Equal(Enumerable.Range(0, 16).ToArray(), SweepCommand.ParseCores("0-15")!);
    [Fact] public void Mixed() => Assert.Equal([0, 3, 8, 9, 10, 11], SweepCommand.ParseCores("0,3,8-11")!);
    [Fact] public void Single() => Assert.Equal([11], SweepCommand.ParseCores("11")!);
    [Fact] public void DuplicatesCollapse() => Assert.Equal([2, 3], SweepCommand.ParseCores("3,2,2-3")!);
    [Theory]
    [InlineData("x")]
    [InlineData("16")]
    [InlineData("5-3")]
    [InlineData("")]
    [InlineData("-1")]
    public void Invalid(string spec) => Assert.Null(SweepCommand.ParseCores(spec));
}
