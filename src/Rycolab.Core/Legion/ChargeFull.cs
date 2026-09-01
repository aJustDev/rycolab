namespace Rycolab.Core.Legion;

/// <summary>
/// Marker for a one-shot full charge: `legion charge full` switches to rapid and
/// drops this file; the guard restores the previous mode once the battery
/// reaches the target and deletes it. Any manual mode change cancels it.
/// </summary>
public sealed record ChargeFull(int Target, string Restore, DateTime Started)
{
    public static ChargeFull? Load() => Journal.ReadJsonFile<ChargeFull>(AppPaths.ChargeFull);
    public void Save() => Journal.WriteJsonFile(AppPaths.ChargeFull, this);
    public static void Delete() => File.Delete(AppPaths.ChargeFull);
    public static bool Pending => File.Exists(AppPaths.ChargeFull);
}
