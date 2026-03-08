namespace SwebKit.Core.Configuration;

public static class AppDataPaths
{
    private static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SwebKit");

    private static string LegacyRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwebKit");

    public static string ProfilesJson => Path.Combine(Root, "profiles.json");
    public static string LegacyProfilesJson => Path.Combine(LegacyRoot, "profiles.json");
    public static string UiStateJson => Path.Combine(Root, "ui-state.json");
    public static string LegacyUiStateJson => Path.Combine(LegacyRoot, "ui-state.json");
    public static string UserSettingsJson => Path.Combine(Root, "user-settings.json");
    public static string ScheduledMessagesJson => Path.Combine(Root, "scheduled-messages.json");

    public static void EnsureDirectoryExists()
    {
        Directory.CreateDirectory(Root);
    }
}
