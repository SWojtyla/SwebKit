namespace SwebKit.Core.Configuration;

public static class AppDataPaths
{
    private static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SwebKit");

    public static string ProfilesJson => Path.Combine(Root, "profiles.json");
    public static string UiStateJson => Path.Combine(Root, "ui-state.json");
    public static string UserSettingsJson => Path.Combine(Root, "user-settings.json");

    public static void EnsureDirectoryExists()
    {
        Directory.CreateDirectory(Root);
    }
}
