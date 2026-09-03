using System.Text.Json;

namespace Rycolab.Core;

/// <summary>
/// Whole-file JSON for state and configuration (profile, config, state,
/// validation...): written atomically, read leniently. History goes to the
/// database (<see cref="Store"/>), never to a file. (Until 0.3.0 this was
/// also the JSONL journal writer; the database replaced it.)
/// </summary>
public static class Journal
{
    /// <summary>Atomic write of a whole JSON file (state, verdict...).</summary>
    public static void WriteJsonFile<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
