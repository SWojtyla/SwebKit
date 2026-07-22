using Microsoft.Extensions.Logging;
using SwebKit.App.Services;
using SwebKit.Core.Configuration;
using SwebKit.Core.Diagnostics;

namespace SwebKit.App.Hosting;

/// <summary>
/// Handles early bootstrap: file logging provider construction, crash handlers,
/// and performance baseline recording. Must be called before any other startup
/// work that could itself throw/log during construction.
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
