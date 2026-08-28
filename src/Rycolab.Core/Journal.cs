using System.Text;
using System.Text.Json;

namespace Rycolab.Core;

/// <summary>
/// JSONL writer flushed to disk on every line.
///
/// Deliberately slow: if the machine hangs in the middle of a test, the last
/// seconds before the hang are the most valuable data there is. An in-memory
/// buffer would take them along.
/// </summary>
public sealed class Journal : IDisposable
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    private readonly FileStream _stream;
    private readonly object _gate = new();

    public string Path { get; }

    public Journal(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                                 bufferSize: 1, FileOptions.WriteThrough);
    }

    public void Write<T>(T record)
    {
        var line = JsonSerializer.Serialize(record, Options) + "\n";
        var bytes = Encoding.UTF8.GetBytes(line);
        lock (_gate)
        {
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush(true);
        }
    }

    public void Dispose() => _stream.Dispose();

    /// <summary>Atomic write of a whole JSON file (state, verdict...).</summary>
    public static void WriteJsonFile<T>(string path, T value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);
    }

    public static T? ReadJsonFile<T>(string path)
    {
        if (!File.Exists(path)) return default;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path)); }
        catch { return default; }
    }
}
