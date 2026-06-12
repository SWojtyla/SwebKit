namespace SwebKit.Core.Configuration;

public static class AppDataPaths
{
    private const string AppDataRootOverrideVariable = "SWEBKIT_APPDATA_ROOT";

    private static string Root
    {
        get
        {
            var overrideRoot = Environment.GetEnvironmentVariable(AppDataRootOverrideVariable);
            if (!string.IsNullOrWhiteSpace(overrideRoot))
                return overrideRoot;

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SwebKit");
        }
    }

    private static string LegacyRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwebKit");

    public static string ProfilesJson => Path.Combine(Root, "profiles.json");
    public static string LegacyProfilesJson => Path.Combine(LegacyRoot, "profiles.json");
    public static string UiStateJson => Path.Combine(Root, "ui-state.json");
    public static string LegacyUiStateJson => Path.Combine(LegacyRoot, "ui-state.json");
    public static string UserSettingsJson => Path.Combine(Root, "user-settings.json");
    public static string ScheduledMessagesJson => Path.Combine(Root, "scheduled-messages.json");
    public static string ReleasesJson => Path.Combine(Root, "releases.json");
    public static string MonitoringAlertsJson => Path.Combine(Root, "monitoring-alerts.json");
    public static string PerformanceBaselineLog => Path.Combine(Root, "logs", "performance-baseline.log");

    public static void EnsureDirectoryExists()
    {
        Directory.CreateDirectory(Root);
    }
}
