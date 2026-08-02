using SwebKit.Core.Configuration;

namespace SwebKit.Core.Diagnostics;

/// <summary>
/// Handles early bootstrap: file logging provider construction and crash handlers. Must be
/// called before any other startup work that could itself throw/log during construction.
/// Shared by both hosts (the MAUI app and the Tauri sidecar) — has no MAUI-specific dependencies.
/// </summary>
public static class AppBootstrap
{
    /// <summary>
    /// Creates the file logger provider and wires crash handlers for
    /// <see cref="AppDomain.UnhandledException"/> and
    /// <see cref="TaskScheduler.UnobservedTaskException"/>.
    /// </summary>
    /// <returns>The <see cref="FileLoggerProvider"/> to add to the logging pipeline.</returns>
    public static FileLoggerProvider ConfigureCrashHandlers(UserSettingsRepository userSettingsRepository)
    {
        var fileLoggerProvider = new FileLoggerProvider(
            () => userSettingsRepository.Settings.Logging,
            AppDataPaths.LogsDirectory);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            fileLoggerProvider.EmergencyWriteAndFlush(LogEntry.ForCrash(ex, e.IsTerminating));
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            fileLoggerProvider.EmergencyWriteAndFlush(LogEntry.ForCrash(e.Exception, isTerminating: false));
            e.SetObserved();
        };

        return fileLoggerProvider;
    }
}
