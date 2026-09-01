using System.IO.Compression;
using System.Security.Cryptography;
using Rycolab.Core.Engines;

namespace Rycolab.Core;

/// <summary>
/// Puts rycolab in %LOCALAPPDATA%\rycolab: binaries, user PATH, y-cruncher,
/// baseline config and the (disabled) scheduled task. Idempotent.
/// </summary>
public static class Installer
{
    public const string YCruncherVersion = "v0.8.7.9547b";
    public const string YCruncherUrl = "https://github.com/Mysticial/y-cruncher/releases/download/v0.8.7.9547b/y-cruncher.v0.8.7.9547b.zip";
    public const string YCruncherSha256 = "3be696c1cc44907f2ea73aac35de4a5d45048255308f34ed4410a743163a4f87";

    /// <summary>Copies the running program's directory to the install dir. Returns false if it is already running from there.</summary>
    public static bool CopyBinaries(Action<string> log)
    {
        var from = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var to = AppPaths.Bin;
        if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
        {
            log($"binaries already in {to}");
            return false;
        }
        Directory.CreateDirectory(to);
        var n = 0;
        foreach (var f in Directory.GetFiles(from))
        {
            var name = Path.GetFileName(f);
            if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(f, Path.Combine(to, name), overwrite: true);
            n++;
        }
        log($"{n} files copied to {to}");
        return true;
    }

    public static void AddToUserPath(Action<string> log)
    {
        var path = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        var parts = path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        // Drop old checkout paths of this tool
        parts.RemoveAll(p => p.Contains(@"Rycolab.Cli\bin", StringComparison.OrdinalIgnoreCase) || p.Contains(@"LegionCoLab.Cli\bin", StringComparison.OrdinalIgnoreCase));
        if (!parts.Any(p => string.Equals(p.TrimEnd('\\'), AppPaths.Bin.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            parts.Add(AppPaths.Bin);
        Environment.SetEnvironmentVariable("Path", string.Join(";", parts), EnvironmentVariableTarget.User);
        log($"user PATH includes {AppPaths.Bin} (new consoles will see it)");
    }

    public static void RemoveFromUserPath()
    {
        var path = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        var parts = path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        parts.RemoveAll(p => string.Equals(p.TrimEnd('\\'), AppPaths.Bin.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        Environment.SetEnvironmentVariable("Path", string.Join(";", parts), EnvironmentVariableTarget.User);
    }

    /// <summary>All the configured engines (default: the ones recommended for this CPU) are present.</summary>
    public static bool HasYCruncher(string? dir = null, IEnumerable<string>? engines = null)
        => YCruncherBinaries.Missing(dir ?? AppPaths.YCruncher, engines ?? YCruncherBinaries.Recommended()).Count == 0;

    /// <summary>Copies the binaries from a directory the user already has (e.g. CoreCycler's test_programs).</summary>
    public static void CopyYCruncher(string fromDir, Action<string> log, IEnumerable<string>? engines = null)
    {
        var src = File.Exists(Path.Combine(fromDir, YCruncherBinaries.Sse3 + ".exe")) ? fromDir : Path.Combine(fromDir, "Binaries");
        var missing = YCruncherBinaries.Missing(src, engines ?? YCruncherBinaries.Recommended());
        if (missing.Count > 0) throw new FileNotFoundException($"y-cruncher binaries missing in {fromDir}: {string.Join(", ", missing.Select(m => m + ".exe"))}");
        Directory.CreateDirectory(AppPaths.YCruncher);
        var n = 0;
        foreach (var f in Directory.GetFiles(src))
        {
            File.Copy(f, Path.Combine(AppPaths.YCruncher, Path.GetFileName(f)), overwrite: true);
            n++;
        }
        log($"{n} y-cruncher files copied from {src}");
    }

    /// <summary>Downloads the official zip, verifies its SHA-256 and extracts Binaries/.</summary>
    public static async Task DownloadYCruncher(Action<string> log, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.Combine(AppPaths.Tools, "y-cruncher"));
        var zip = Path.Combine(AppPaths.Tools, $"y-cruncher.{YCruncherVersion}.zip");

        if (!File.Exists(zip) || !await Sha256Matches(zip, YCruncherSha256))
        {
            log($"downloading y-cruncher {YCruncherVersion} (45 MB) from GitHub...");
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(10);
            using var resp = await http.GetAsync(YCruncherUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using (var fs = new FileStream(zip + ".part", FileMode.Create, FileAccess.Write))
                await resp.Content.CopyToAsync(fs, ct);
            File.Move(zip + ".part", zip, overwrite: true);
        }

        if (!await Sha256Matches(zip, YCruncherSha256))
        {
            File.Delete(zip);
            throw new InvalidDataException("the downloaded y-cruncher zip does not match the expected SHA-256; deleted.");
        }
        log("checksum ok");

        Directory.CreateDirectory(AppPaths.YCruncher);
        using var archive = ZipFile.OpenRead(zip);
        var n = 0;
        foreach (var e in archive.Entries)
        {
            var i = e.FullName.IndexOf("/Binaries/", StringComparison.OrdinalIgnoreCase);
            if (i < 0 || e.FullName.EndsWith('/')) continue;
            var rel = e.FullName[(i + "/Binaries/".Length)..];
            var target = Path.GetFullPath(Path.Combine(AppPaths.YCruncher, rel));
            if (!target.StartsWith(Path.GetFullPath(AppPaths.YCruncher), StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, overwrite: true);
            n++;
        }
        log($"{n} files extracted to {AppPaths.YCruncher}");
        if (!HasYCruncher()) throw new FileNotFoundException("the zip did not contain the expected binaries.");
    }

    private static async Task<bool> Sha256Matches(string file, string expected)
    {
        await using var fs = File.OpenRead(file);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
        return hash == expected.ToLowerInvariant();
    }

    // ---- the inpout kernel driver -------------------------------------------
    // ZenStates.Core 1.0.1 refuses to initialise without inpoutx64.dll even
    // though it embeds PawnIO modules (checked 2026-09-01: "Can't load DLL
    // inpoutx64.dll"), so the driver stays. Its service outlives the install.

    public const string InpoutService = "inpoutx64";

    public static bool InpoutServiceExists() => Sc($"query {InpoutService}") == 0;

    /// <summary>Stops and deletes the service and its .sys. Best effort; every step is reported.</summary>
    public static void RemoveInpoutService(Action<string> log)
    {
        log($"sc stop {InpoutService}: {(Sc($"stop {InpoutService}") == 0 ? "ok" : "not running")}");
        log($"sc delete {InpoutService}: {(Sc($"delete {InpoutService}") == 0 ? "ok" : "FAILED")}");
        var sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "inpoutx64.sys");
        try { if (File.Exists(sys)) { File.Delete(sys); log($"deleted {sys}"); } }
        catch (Exception ex) { log($"{sys} stays ({ex.Message}); a reboot releases it"); }
    }

    private static int Sc(string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("sc.exe", arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    /// <summary>The margin the BIOS leaves on the cores: what they all read now, or the most common value.</summary>
    public static int ReadBaseline(CoController co, Action<string> log)
    {
        var margins = co.ReadAll().Where(r => r.Margin.HasValue).Select(r => r.Margin!.Value).ToList();
        if (margins.Count == 0) throw new InvalidOperationException("no core could be read.");
        var groups = margins.GroupBy(m => m).OrderByDescending(g => g.Count()).ToList();
        var baseline = groups[0].Key;
        if (groups.Count > 1)
            log($"cores are not uniform ({string.Join("/", groups.Select(g => $"{g.Key} x{g.Count()}"))}); using {baseline}. Reboot and run install again for a clean read.");
        Safety.ValidateMargin(baseline, "baseline");
        return baseline;
    }
}
