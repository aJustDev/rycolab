namespace Rycolab.Core;

/// <summary>
/// Where rycolab keeps its things: %LOCALAPPDATA%\rycolab (override with the
/// RYCOLAB_HOME environment variable). Nothing is written next to the
/// executable or inside a repository checkout.
/// </summary>
public static class AppPaths
{
    public static string Data =>
        Environment.GetEnvironmentVariable("RYCOLAB_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rycolab");

    public static string Bin => Path.Combine(Data, "bin");
    public static string Exe => Path.Combine(Bin, "rycolab.exe");
    public static string Tools => Path.Combine(Data, "tools");
    public static string YCruncher => Path.Combine(Tools, "y-cruncher", "Binaries");
    public static string Profile => Path.Combine(Data, "profile.json");
    public static string Config => Path.Combine(Data, "config.json");
    public static string State => Path.Combine(Data, "state.json");
    public static string Validation => Path.Combine(Data, "validation.json");
    public static string Guard => Path.Combine(Data, "guard");
    public static string Campaigns => Path.Combine(Data, "campaigns");
    public static string CurrentCampaign => Path.Combine(Data, "current-campaign");
    public static string ChargeFull => Path.Combine(Data, "charge-full.json");
    public static string BatteryDesign => Path.Combine(Data, "battery-design.json");

    public static string Campaign(string name)
        => Path.IsPathRooted(name) ? name : Path.Combine(Campaigns, name);

    public static void EnsureData() => Directory.CreateDirectory(Data);
}
