using System.Runtime.Intrinsics.X86;

namespace Rycolab.Core.Engines;

/// <summary>
/// Which y-cruncher binaries to use on this CPU. `04-P4P` (SSE3) is the load
/// that reaches fMax on every Zen; the second engine is the widest vector
/// binary the CPU can run: `24-ZN5 ~ Komari` (AVX-512) on Zen 4/5,
/// `19-ZN2 ~ Kagari` (AVX2) on Zen 2/3. Names are the official 0.8.7 ones.
/// </summary>
public static class YCruncherBinaries
{
    public const string Sse3 = "04-P4P";
    public const string Avx512 = "24-ZN5 ~ Komari";
    public const string Avx2 = "19-ZN2 ~ Kagari";

    public static bool HasAvx512 => Avx512F.IsSupported;

    public static string[] Recommended() => [Sse3, HasAvx512 ? Avx512 : Avx2];

    public static string Why() => HasAvx512 ? $"AVX-512 available -> {Avx512}" : $"no AVX-512 -> {Avx2}";

    /// <summary>Engines whose exe is not in the directory.</summary>
    public static List<string> Missing(string dir, IEnumerable<string> engines)
        => engines.Where(e => !File.Exists(Path.Combine(dir, e + ".exe"))).ToList();
}
