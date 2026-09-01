namespace Rycolab.Cli;

/// <summary>Minimal argument parsing. Not worth a dependency.</summary>
public sealed class Args
{
    /// <summary>
    /// Options that never take a value, so `--plain campaign1` leaves
    /// `campaign1` as a positional instead of swallowing it. Anything else
    /// takes the next token (`--core 3`) or `=` (`--core=3`); a flag that
    /// also accepts a value (`--md`, `--json`) is not listed and takes the
    /// next token when there is one.
    /// </summary>
    public static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "plain", "quick", "yes", "accept", "resume", "force", "dry-run", "once", "follow", "health", "battery",
        "no-task", "no-suspend", "no-windows", "close-apps", "raw", "rebuild", "sensors", "write-test", "no-compare",
        "new", "purge", "all",
    };

    private readonly Dictionary<string, string?> _opts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positional = [];

    public Args(IEnumerable<string> argv)
    {
        string? pending = null;
        foreach (var a in argv)
        {
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                if (pending is not null) _opts[pending] = null;
                pending = null;
                var eq = a.IndexOf('=');
                if (eq > 0) _opts[a[2..eq]] = a[(eq + 1)..];
                else if (Flags.Contains(a[2..])) _opts[a[2..]] = null;
                else pending = a[2..];
            }
            else if (pending is not null)
            {
                _opts[pending] = a;
                pending = null;
            }
            else _positional.Add(a);
        }
        if (pending is not null) _opts[pending] = null;
    }

    public IReadOnlyList<string> Positional => _positional;
    public bool Has(string name) => _opts.ContainsKey(name);
    public string? Get(string name) => _opts.GetValueOrDefault(name);

    public string Get(string name, string fallback) => _opts.GetValueOrDefault(name) ?? fallback;

    public int? GetInt(string name)
        => int.TryParse(_opts.GetValueOrDefault(name), out var v) ? v : null;
}
