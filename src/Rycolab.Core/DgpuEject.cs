namespace Rycolab.Core;

/// <summary>
/// Marker for a pending dGPU ejection: the battery profile drops it when the
/// card refuses to leave after the switch to iGPU-only. The guard nudges the
/// EC every tick (NotifyDGPUStatus makes it retry the ejection, which lands
/// once the card has idled for ~2-3 min - measured 2026-09-01) and deletes
/// the marker on success, on AC restore, or after giving up.
/// </summary>
public sealed record DgpuEject(DateTime Started)
{
    public static DgpuEject? Load() => Journal.ReadJsonFile<DgpuEject>(AppPaths.DgpuEject);
    public void Save() => Journal.WriteJsonFile(AppPaths.DgpuEject, this);
    public static void Delete() { try { File.Delete(AppPaths.DgpuEject); } catch { } }
}
