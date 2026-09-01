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

    /// <summary>
    /// CLI shorthand ("p4p", "zn5", "zn2") or an exact binary name. On the
    /// 9955HX3D the definitive campaign's 26 positives all came from ZN5 and
    /// P4P never failed, so a single-engine sweep loses nothing there.
    /// </summary>
    public static string Resolve(string spec) => spec.ToLowerInvariant() switch
    {
        "p4p" or "sse3" => Sse3,
        "zn5" or "avx512" => Avx512,
        "zn2" or "avx2" => Avx2,
        _ => spec,
    };

    public static string Why() => HasAvx512 ? $"AVX-512 available -> {Avx512}" : $"no AVX-512 -> {Avx2}";

    /// <summary>Engines whose exe is not in the directory.</summary>
    public static List<string> Missing(string dir, IEnumerable<string> engines)
        => engines.Where(e => !File.Exists(Path.Combine(dir, e + ".exe"))).ToList();
}
