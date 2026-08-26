namespace LegionCoLab.Cli;

/// <summary>Analisis minimo de argumentos. No merece una dependencia.</summary>
public sealed class Args
{
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
                var eq = a.IndexOf('=');
                if (eq > 0)
                {
                    _opts[a[2..eq]] = a[(eq + 1)..];
                    pending = null;
                }
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
