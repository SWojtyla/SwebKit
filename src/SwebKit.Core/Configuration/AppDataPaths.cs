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

    // Logs, journals and other machine-local diagnostics live under LocalApplicationData:
    // Roaming is synced with the user profile on domain setups, which is wrong for
    // per-machine artifacts. The override still applies so test sandboxes capture logs too.
    private static string LocalRoot
    {
        get
        {
            var overrideRoot = Environment.GetEnvironmentVariable(AppDataRootOverrideVariable);
            if (!string.IsNullOrWhiteSpace(overrideRoot))
                return overrideRoot;

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwebKit");
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
    /// <summary>
    /// Crash journal for Service Bus power ops (reach-message). An entry left "running" here after
    /// a restart marks an interrupted operation — the broker-side stamp scan then rediscovers how
    /// many messages actually parked.
    /// </summary>
    public static string SbOperationsJournalJson => Path.Combine(Root, "sb-operations-journal.json");
    public static string MonitoringAlertsJson => Path.Combine(Root, "monitoring-alerts.json");
    public static string MonitoringInsightsJson => Path.Combine(Root, "monitoring-insights.json");
    public static string MonitoringSilencesJson => Path.Combine(Root, "monitoring-silences.json");
    public static string MonitoringHistoryJson => Path.Combine(Root, "monitoring-history.json");
    /// <summary>Persisted thumbs-down regression cases (agent-colleague item 5) — redacted
    /// exchange context flushed by <c>POST /api/agent/feedback</c>, exported from Settings → AI
    /// Agent for prompt tuning.</summary>
    public static string AgentFeedbackJson => Path.Combine(Root, "agent-feedback.json");
    public static string CollectionsJson => Path.Combine(Root, "collections.json");
    public static string EnvironmentsJson => Path.Combine(Root, "environments.json");
    public static string ApiLinkedRootsJson => Path.Combine(Root, "api-linked-roots.json");
    public static string SqlQueriesJson => Path.Combine(Root, "sql-queries.json");
    public static string PerformanceBaselineLog => Path.Combine(LocalRoot, "logs", "performance-baseline.log");
    public static string LogsDirectory => Path.Combine(LocalRoot, "logs");
    /// <summary>Runtime discovery file external MCP clients read to find the sidecar's bound
    /// address — machine-local like logs, since it describes a process on this machine only.</summary>
    public static string SidecarEndpointJson => Path.Combine(LocalRoot, "sidecar-endpoint.json");

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
            var canonicalRoot = Path.GetFullPath(Root);

            foreach (var file in Directory.EnumerateFiles(canonicalRoot, "*.tmp", SearchOption.TopDirectoryOnly))
            {
                TryDeleteIfOlderThan(file, cutoff, canonicalRoot);
            }
        }
        catch
        {
            // Best-effort — must never throw.
        }
    }

    private static void TryDeleteIfOlderThan(string file, DateTime cutoff, string canonicalRoot)
    {
        try
        {
            // Same containment discipline as LogRetentionCleanupService: validate the leaf
            // filename and re-derive the delete target from the known-safe root rather than
            // trusting the enumerated path string, even though enumeration over a fixed
            // directory should never produce a traversal-shaped name.
            var fileName = Path.GetFileName(file);
            if (string.IsNullOrEmpty(fileName) ||
                fileName.Contains("..", StringComparison.Ordinal) ||
                fileName.Contains('/') ||
                fileName.Contains('\\'))
                return;

            var safePath = Path.GetFullPath(Path.Combine(canonicalRoot, fileName));
            if (!safePath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return;

            if (File.GetLastWriteTime(safePath) < cutoff)
                File.Delete(safePath);
        }
        catch
        {
            // File might be locked or in use — skip it.
        }
    }
}
