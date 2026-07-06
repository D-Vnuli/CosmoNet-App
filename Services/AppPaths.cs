namespace CosmoNet.App.Services;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CosmoNet");

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static string GeneratedConfigPath => Path.Combine(DataDirectory, "sing-box.json");

    public static string BundledSingBoxPath => Path.Combine(
        AppContext.BaseDirectory,
        "Resources",
        "sing-box",
        "sing-box.exe");

    public static void EnsureDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
    }
}
