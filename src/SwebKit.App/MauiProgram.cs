using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Hosting;
using SwebKit.App.Services;
using SwebKit.Core.Configuration;
using SwebKit.Core.Diagnostics;

namespace SwebKit.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        PerformanceBaselineRecorder.Record(nameof(MauiProgram), "Perf startup CreateMauiApp entered");
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Clean up orphaned temp files from interrupted atomic saves (e.g. previous process
        // killed mid-write). Best-effort, never throws, only touches files older than 1 hour.
        AppDataPaths.CleanupOrphanedTempFiles();

        // Structured file logging + crash handlers — constructed and wired as early as possible,
        // before any other startup work that could itself throw/log during construction.
        // See docs/features/active/structured-file-logging/backend.md "Crash-Safe Emergency Path" / "Startup Wiring".
        var userSettingsRepository = new UserSettingsRepository();
        var fileLoggerProvider = AppBootstrap.ConfigureCrashHandlers(userSettingsRepository);

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddFluentUIComponents();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddDebug();
#endif
        builder.Logging.AddProvider(fileLoggerProvider);
        // The FileLoggerProvider does its own level filtering based on user settings
        // (LoggingSettings.MinimumLevel). Without this filter, the factory's default minimum
        // level (Warning in release builds) silently blocks Information/Debug entries the user
        // explicitly enabled — and no log files are ever created.
        builder.Logging.AddFilter<FileLoggerProvider>(_ => true);

        // Feature module registration
        builder.Services.AddSwebKitCore(userSettingsRepository);
        builder.Services.AddSwebKitAppServices();
        builder.Services.AddSwebKitAlerts();
        builder.Services.AddSwebKitDemoClients();
        builder.Services.AddSwebKitObservability();
        builder.Services.AddSwebKitDevOps();
        builder.Services.AddSwebKitIncidentTimeline();
        builder.Services.AddSwebKitApiClient();
        builder.Services.AddSwebKitConnectionWarmup();
        builder.Services.AddSwebKitAgents();

        var app = builder.Build();

        // Fire-and-forget startup log retention cleanup — same "perf startup" style as
        // PerformanceBaselineRecorder/MonitoringMigrationService: never delays first paint,
        // never throws out to the caller.
        _ = Task.Run(async () =>
        {
            try
            {
                var retentionCleanup = app.Services.GetRequiredService<Core.Diagnostics.ILogRetentionCleanupService>();
                await retentionCleanup.RunAsync();
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown; nothing to do.
            }
            catch
            {
                // Best-effort startup cleanup — must never surface failures to the UI.
            }
        });

        PerformanceBaselineRecorder.Record(nameof(MauiProgram), "Perf startup CreateMauiApp completed");
        return app;
    }
}
