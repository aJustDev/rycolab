using Rycolab.Core.Engines;

namespace Rycolab.Tests;

/// <summary>The compute-error criterion against real y-cruncher output from the reference campaign (fixtures/).</summary>
public class YCruncherErrorTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static string? FirstError(string file) => File.ReadLines(Fixture(file)).FirstOrDefault(YCruncherEngine.IsErrorLine)?.Trim();

    [Fact]
    public void ConvolutionFailureIsCaughtOnItsFirstLine()
        => Assert.Equal("Exception Encountered: ConvolutionFailedException", FirstError("ycruncher-error-convolution.txt"));

    [Fact]
    public void ChecksumMismatchIsCaughtOnItsFirstLine()
        => Assert.Equal("Exception Encountered: AlgorithmFailedException", FirstError("ycruncher-error-checksum.txt"));

    [Fact]
    public void CleanRunHasNoErrorLine()
    {
        Assert.Null(FirstError("ycruncher-clean.txt"));
        Assert.Contains(File.ReadLines(Fixture("ycruncher-clean.txt")), l => l.Contains("Passed"));
    }

    [Theory]
    [InlineData("  6   Stop on Error:      Enabled")]
    [InlineData("Running SFTv4: Passed  Test Speed:  6.91 * 10^09  bits / sec")]
    [InlineData("Stress test completed with 0 errors.")]
    [InlineData("No errors encountered.")]
    public void BenignLines(string line) => Assert.False(YCruncherEngine.IsErrorLine(line));

    [Theory]
    [InlineData("Checksum Mismatch")]
    [InlineData("Running SVT: Failed  Test Speed:  3.38 * 10^09  bits / sec")]
    [InlineData("Error(s) encountered on logical core 0.")]
    [InlineData("Bottom word mismatch")]
    public void ErrorLines(string line) => Assert.True(YCruncherEngine.IsErrorLine(line));
}

public class BinariesTests
{
    [Theory]
    [InlineData("p4p", YCruncherBinaries.Sse3)]
    [InlineData("ZN5", YCruncherBinaries.Avx512)]
    [InlineData("avx2", YCruncherBinaries.Avx2)]
    [InlineData("24-ZN5 ~ Komari", YCruncherBinaries.Avx512)]
    public void Resolve(string spec, string expected) => Assert.Equal(expected, YCruncherBinaries.Resolve(spec));

    [Fact]
    public void MissingListsAbsentExes()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rycolab-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, YCruncherBinaries.Sse3 + ".exe"), "");
            Assert.Equal([YCruncherBinaries.Avx512], YCruncherBinaries.Missing(dir, [YCruncherBinaries.Sse3, YCruncherBinaries.Avx512]));
        }
        finally { Directory.Delete(dir, true); }
    }
}
