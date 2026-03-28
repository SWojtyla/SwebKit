using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Platforms.Windows;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;
using SwebKit.DevOps;
using SwebKit.Observability;
using SelectionContext = SwebKit.App.Services.SelectionContext;

namespace SwebKit.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddFluentUIComponents();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // Core infrastructure
        builder.Services.AddSingleton<ICredentialStore, WindowsCredentialStore>();
        builder.Services.AddSingleton<IAppEventBus, AppEventBus>();
        builder.Services.AddSingleton<ITaskQueue, TaskQueueService>();
        builder.Services.AddSingleton<ProfileRepository>();
        builder.Services.AddSingleton<UiStateRepository>();
        builder.Services.AddSingleton<ScheduledMessageRepository>();
        builder.Services.AddSingleton<AppStateService>();

        // App UI services
        builder.Services.AddSingleton<IConnectionStateService, ConnectionStateService>();
        builder.Services.AddSingleton<TabService>();
        builder.Services.AddSingleton<CommandRegistry>();
        builder.Services.AddSingleton<INotificationService, NotificationService>();
        builder.Services.AddSingleton<IPortForwardSessionService, PortForwardSessionService>();
        builder.Services.AddSingleton<ISelectionContext, SelectionContext>();

        // Pod Health Monitor
#if WINDOWS
        builder.Services.AddSingleton<IWindowsNotificationService, WindowsToastNotificationService>();
#else
        builder.Services.AddSingleton<IWindowsNotificationService, NullWindowsNotificationService>();
#endif
        builder.Services.AddSingleton<IPodHealthMonitorService, PodHealthMonitorService>();

        // Demo clients (singletons; pages select real vs. demo based on AppStateService.UseDemoData)
        builder.Services.AddSingleton<DemoStorageClient>();

        // Observability — real resource discovery (singleton for caching); providers are created per-resource by the page
        builder.Services.AddSingleton<IObservabilityResourceDiscovery, AppInsightsDiscoveryService>();
        builder.Services.AddSingleton<IGuidedKqlCompiler, GuidedKqlCompiler>();

        // DevOps / Releases
        builder.Services.AddSingleton<DevOpsAuthHandler>();
        builder.Services.AddHttpClient("AzureDevOps")
            .AddHttpMessageHandler<DevOpsAuthHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromSeconds(1);
            });
        builder.Services.AddSingleton<DevOpsClient>();
        builder.Services.AddSingleton<DemoDevOpsClient>();
        builder.Services.AddSingleton<ReleaseRepository>();
        builder.Services.AddSingleton<PageDataCache>();

        return builder.Build();
    }
}
