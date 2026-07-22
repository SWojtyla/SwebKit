using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Hosting;
using SwebKit.App.Services;
using SwebKit.Core.Configuration;

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
