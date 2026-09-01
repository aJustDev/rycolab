using Rycolab.Core;

namespace Rycolab.Tests;

public class BenchLogTests
{
    private static string Csv(params string[] rows)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rycolab-test-{Guid.NewGuid():N}.csv");
        File.WriteAllLines(path, new[] { $"{BenchLog.Time},{BenchLog.Elapsed},{BenchLog.PackagePower},{BenchLog.Tctl},{BenchLog.Ac},{BenchLog.BatteryW},{BenchLog.BatteryPct},{BenchLog.BatteryWh}" }.Concat(rows));
        return path;
    }

    [Fact]
    public void ColumnsStartWithTheFixedOnesThenPerCore()
    {
        var cols = BenchLog.Columns(2);
        Assert.Equal(BenchLog.Time, cols[0]);
        Assert.Equal(["eff_c0_mhz", "eff_c1_mhz", "vcore_c0_v", "vcore_c1_v"], cols.TakeLast(4));
    }

    [Fact]
    public void ReadFiltersRowsAndSkipsEmptyCells()
    {
        var path = Csv(
            "2026-09-01 10:00:00,0,20.5,45.0,1,,,",
            "2026-09-01 10:00:02,2,120.0,80.0,1,,,",
            "2026-09-01 10:00:04,4,140.0,85.0,1,,,",
            "2026-09-01 10:00:06,6,bad,,1,,,");
        try
        {
            var d = BenchLog.Read(path, row => row.TryGetValue(BenchLog.PackagePower, out var p) && p > 100, out var rows, out var kept);
            Assert.Equal(4, rows);
            Assert.Equal(2, kept);
            var pkg = BenchLog.Of(d, BenchLog.PackagePower)!;
            Assert.Equal(130.0, pkg.Mean);
            Assert.Equal(140.0, pkg.Max);
            Assert.Null(BenchLog.Of(d, BenchLog.BatteryW));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SummaryComparesAgainstABaselineAndEstimatesRuntime()
    {
        var after = Csv("2026-09-01 10:00:00,0,15.0,50.0,0,12.0,50.0,40.0", "2026-09-01 10:00:02,2,15.0,50.0,0,14.0,49.9,39.9");
        var before = Csv("2026-09-01 09:00:00,0,15.0,50.0,0,16.0,60.0,48.0");
        try
        {
            var a = BenchLog.Read(after, null, out var rows, out var kept);
            var b = BenchLog.Read(before, null, out _, out _);
            var md = BenchLog.Summary("after", a, rows, kept, "everything", "before", b);
            Assert.Contains("| Battery discharge [W] | 16.00 | 13.00 | -3.00 | 14.00 |", md);
            Assert.Contains("Runtime at the mean discharge from a full battery (80 Wh): 6.2 h (before: 5.0 h; power -18.8 %).", md);
        }
        finally { File.Delete(after); File.Delete(before); }
    }
}
