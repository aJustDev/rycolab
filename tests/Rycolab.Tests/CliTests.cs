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

    [Fact]
    public void BooleanFlagsNeverSwallowTheNextToken()
    {
        var a = new Args(["--plain", "campaign1", "--all", "--yes"]);
        Assert.True(a.Has("plain"));
        Assert.Null(a.Get("plain"));
        Assert.True(a.Has("all"));
        Assert.True(a.Has("yes"));
        Assert.Equal(["campaign1"], a.Positional);
    }

    [Fact]
    public void ValueOptionsTakeTheNextTokenOrEquals()
    {
        var a = new Args(["--compare", "other.json", "--md=report.md", "--json", "--once"]);
        Assert.Equal("other.json", a.Get("compare"));
        Assert.Equal("report.md", a.Get("md"));
        Assert.True(a.Has("json"));
        Assert.Null(a.Get("json"));   // followed by another option: no value
        Assert.True(a.Has("once"));
    }

    [Fact]
    public void FlagWithEqualsStillCarriesAValue()
    {
        var a = new Args(["--plain=1"]);
        Assert.Equal("1", a.Get("plain"));
    }

    [Fact]
    public void EveryFlagIsKnownByName()
    {
        // The list is the contract with the commands: a typo here would silently turn a flag back into a value option.
        foreach (var f in new[] { "plain", "quick", "yes", "accept", "resume", "force", "dry-run", "once", "follow", "purge", "all" })
            Assert.Contains(f, Args.Flags);
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
