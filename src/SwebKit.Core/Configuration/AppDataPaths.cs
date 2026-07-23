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
    public static string CollectionsJson => Path.Combine(Root, "collections.json");
    public static string EnvironmentsJson => Path.Combine(Root, "environments.json");
    public static string ApiLinkedRootsJson => Path.Combine(Root, "api-linked-roots.json");
    public static string PerformanceBaselineLog => Path.Combine(Root, "logs", "performance-baseline.log");
    public static string LogsDirectory => Path.Combine(Root, "logs");

    public static string FeatureLogFile(string feature, DateOnly date) =>
        Path.Combine(LogsDirectory, $"{feature}-{date:yyyy-MM-dd}.log");

    public static void EnsureDirectoryExists()
    {
        Directory.CreateDirectory(Root);
    }

    /// <summary>
    /// Best-effort removal of orphaned temp files left behind by interrupted atomic saves
    /// (e.g. process killed mid-write). Covers both app-created <c>*.tmp</c> files and
    /// Windows Reserved Files (<c>*~RF*.TMP</c>). Only deletes files older than 1 hour
    /// to avoid touching in-progress writes. Never throws.
    /// </summary>
    public static void CleanupOrphanedTempFiles()
    {
        try
        {
            if (!Directory.Exists(Root))
                return;

            var cutoff = DateTime.Now.AddHours(-1);

            foreach (var file in Directory.EnumerateFiles(Root, "*.tmp", SearchOption.TopDirectoryOnly))
            {
                TryDeleteIfOlderThan(file, cutoff);
            }
        }
        catch
        {
            // Best-effort — must never throw.
        }
    }

    private static void TryDeleteIfOlderThan(string file, DateTime cutoff)
    {
        try
        {
            if (File.GetLastWriteTime(file) < cutoff)
                File.Delete(file);
        }
        catch
        {
            // File might be locked or in use — skip it.
        }
    }
}
