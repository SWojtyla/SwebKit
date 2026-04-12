using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Platforms.Windows;
using SwebKit.App.Services;
using SwebKit.Azure.ServiceBus.IncidentTimeline;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;
using SwebKit.DevOps;
using SwebKit.DevOps.IncidentTimeline;
using SwebKit.Kubernetes.IncidentTimeline;
using SwebKit.Observability;
using SwebKit.Observability.IncidentTimeline;
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
        builder.Services.AddSingleton<IAksClientBootstrapper, AksClientBootstrapper>();
        builder.Services.AddSingleton<IShellErrorPresenter, ShellErrorPresenter>();
        builder.Services.AddSingleton<IPortForwardSessionService, PortForwardSessionService>();
        builder.Services.AddSingleton<ISelectionContext, SelectionContext>();
        builder.Services.AddSingleton<IServiceBusNamespaceBootstrapper, ServiceBusNamespaceBootstrapper>();
        builder.Services.AddSingleton<TrayLifecycleState>();

        // Pod Health Monitor
#if WINDOWS
        builder.Services.AddSingleton<IWindowsNotificationService, WindowsToastNotificationService>();
        builder.Services.AddSingleton<ITrayLifecycleService, WindowsTrayLifecycleService>();
#else
        builder.Services.AddSingleton<IWindowsNotificationService, NullWindowsNotificationService>();
        builder.Services.AddSingleton<ITrayLifecycleService, NullTrayLifecycleService>();
#endif
        builder.Services.AddSingleton<IPodHealthMonitorService, PodHealthMonitorService>();

        // Demo clients (singletons; pages select real vs. demo based on AppStateService.UseDemoData)
        builder.Services.AddSingleton<DemoStorageClient>();

        // Observability — real resource discovery (singleton for caching); providers are created per-resource by the factory seam
        builder.Services.AddSingleton<IObservabilityResourceDiscovery, AppInsightsDiscoveryService>();
        builder.Services.AddSingleton<IObservabilityProviderFactory, ObservabilityProviderFactory>();
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
        builder.Services.AddSingleton<IDevOpsClientFactory, DevOpsClientFactory>();
        builder.Services.AddSingleton<DemoDevOpsClient>();
        builder.Services.AddSingleton<ReleaseRepository>();
        builder.Services.AddSingleton<PageDataCache>();
        builder.Services.AddSingleton<IIncidentTimelineSignalSource, AksTimelineSignalSource>();
        builder.Services.AddSingleton<IIncidentTimelineSignalSource, AppInsightsTimelineSignalSource>();
        builder.Services.AddSingleton<IIncidentTimelineSignalSource, ServiceBusEvidenceSignalSource>();
        builder.Services.AddSingleton<IIncidentTimelineSignalSource, DevOpsReleaseTimelineSignalSource>();
        builder.Services.AddSingleton<IIncidentTimelineService, IncidentTimelineService>();

        return builder.Build();
    }
}
