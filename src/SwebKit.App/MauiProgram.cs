using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Platforms.Windows;
using SwebKit.App.Services;
using SwebKit.Azure.ServiceBus;
using SwebKit.Azure.Storage;
using SwebKit.Azure.ServiceBus.IncidentTimeline;
using SwebKit.Azure;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;
using SwebKit.DevOps;
using SwebKit.DevOps.IncidentTimeline;
using SwebKit.Kubernetes.AksClient;
using SwebKit.Kubernetes.IncidentTimeline;
using SwebKit.Observability;
using SwebKit.Observability.IncidentTimeline;
using SwebKit.Redis;
using SelectionContext = SwebKit.App.Services.SelectionContext;

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

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddFluentUIComponents();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddDebug();
#endif

        // Core infrastructure
        builder.Services.AddSingleton<ICredentialStore, WindowsCredentialStore>();
        builder.Services.AddSingleton<IAppEventBus, AppEventBus>();
        builder.Services.AddSingleton<ITaskQueue, TaskQueueService>();
        builder.Services.AddSingleton<ProfileRepository>();
        builder.Services.AddSingleton<UiStateRepository>();
        builder.Services.AddSingleton<UserSettingsRepository>();
        builder.Services.AddSingleton<PinnedPortForwardService>();
        builder.Services.AddSingleton<ScheduledMessageRepository>();
        builder.Services.AddSingleton<AppStateService>();
        builder.Services.AddSingleton<ConfigurationBundleService>();
        builder.Services.AddSingleton<CollectionRepository>();
        builder.Services.AddSingleton<EnvironmentRepository>();
        builder.Services.AddSingleton<IConfigurationHealthService, ConfigurationHealthService>();
        builder.Services.AddSingleton<IConfigurationProbeService, ConfigurationProbeService>();

        // App UI services
        builder.Services.AddSingleton<IConnectionStateService, ConnectionStateService>();
        builder.Services.AddSingleton<TabService>();
        builder.Services.AddSingleton<CommandRegistry>();
        builder.Services.AddScoped<OperatorWorkspaceService>();
        builder.Services.AddSingleton<INotificationService, NotificationService>();
        builder.Services.AddSingleton<IAksClientBootstrapper, AksClientBootstrapper>();
        builder.Services.AddSingleton<IShellErrorPresenter, ShellErrorPresenter>();
        builder.Services.AddSingleton<IPortForwardSessionService, PortForwardSessionService>();
        builder.Services.AddSingleton<ISelectionContext, SelectionContext>();
        builder.Services.AddSingleton<IServiceBusNamespaceBootstrapper, ServiceBusNamespaceBootstrapper>();
        builder.Services.AddSingleton<IServiceBusClientFactory, ServiceBusClientFactory>();
        builder.Services.AddSingleton<IAksClientFactory, AksClientFactory>();
        builder.Services.AddSingleton<IStorageClientFactory, StorageClientFactory>();
        builder.Services.AddSingleton<IRedisClientFactory, RedisClientFactory>();
        builder.Services.AddSingleton<IOperatorResourceSearchProvider, ServiceBusResourceSearchProvider>();
        builder.Services.AddSingleton<IOperatorResourceSearchProvider, AksResourceSearchProvider>();
        builder.Services.AddSingleton<IOperatorResourceSearchProvider, StorageResourceSearchProvider>();
        builder.Services.AddSingleton<IOperatorResourceSearchProvider, RedisResourceSearchProvider>();
        builder.Services.AddSingleton<IOperatorResourceSearchProvider, ObservabilityResourceSearchProvider>();
        builder.Services.AddSingleton<IOperatorResourceSearchProvider, IncidentTimelineSearchProvider>();
        builder.Services.AddSingleton<TrayLifecycleState>();

#if WINDOWS
        builder.Services.AddSingleton<IWindowsNotificationService, WindowsToastNotificationService>();
        builder.Services.AddSingleton<ITrayLifecycleService, WindowsTrayLifecycleService>();
#else
        builder.Services.AddSingleton<IWindowsNotificationService, NullWindowsNotificationService>();
        builder.Services.AddSingleton<ITrayLifecycleService, NullTrayLifecycleService>();
#endif

        // Alert Monitor (replaces PodHealthMonitorService)
        builder.Services.AddSingleton<IMonitoringConnectionPool, MonitoringConnectionPool>();
        builder.Services.AddSingleton<IAlertRuleRepository, AlertRuleRepository>();
        builder.Services.AddSingleton<IAlertSignalSource, AksPodHealthSignalSource>();
        builder.Services.AddSingleton<IAlertSignalSource, AksPodRestartRateSignalSource>();
        builder.Services.AddSingleton<IAlertSignalSource, AksNamespaceHealthScoreSignalSource>();
        builder.Services.AddSingleton<IAlertSignalSource, ServiceBusDlqSignalSource>();
        builder.Services.AddSingleton<IAlertSignalSource, ServiceBusActiveDepthSignalSource>();
        builder.Services.AddSingleton<IAlertSignalSource, ServiceBusDeadSubscriptionSignalSource>();
        builder.Services.AddSingleton<IAlertSignalSource, RedisMemorySignalSource>();
        builder.Services.AddSingleton<IAlertSignalSource, RedisConnectedClientsSignalSource>();
        builder.Services.AddSingleton<IAlertMonitorService, AlertMonitorService>();
        builder.Services.AddSingleton<MonitoringMigrationService>();
        // Null stub retains DashboardPage + legacy AKS sub-component DI compatibility
        builder.Services.AddSingleton<IPodHealthMonitorService, NullPodHealthMonitorService>();

        // Demo clients (singletons; pages select real vs. demo based on AppStateService.UseDemoData)
        builder.Services.AddSingleton<DemoAksClient>();
        builder.Services.AddSingleton(new DemoRedisClient(0));
        builder.Services.AddSingleton<DemoStorageClient>();

        // Observability — real resource discovery (singleton for caching); providers are created per-resource by the factory seam
        builder.Services.AddSingleton<IObservabilityResourceDiscovery, AppInsightsDiscoveryService>();
        builder.Services.AddSingleton<IObservabilityProviderFactory, ObservabilityProviderFactory>();
        builder.Services.AddSingleton<IGuidedKqlCompiler, GuidedKqlCompiler>();
        builder.Services.AddSingleton<IObservabilityExplainerService, ObservabilityExplainerService>();

        // DevOps / Releases
        builder.Services.AddTransient<DevOpsAuthHandler>();
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
        builder.Services.AddSingleton<IIncidentInvestigationSeedResolver, IncidentInvestigationSeedResolver>();
        builder.Services.AddSingleton<IIncidentSnapshotExporter, IncidentSnapshotExporter>();
        builder.Services.AddSingleton<IIncidentMappingProposalGenerator, IncidentMappingProposalGenerator>();
        builder.Services.AddScoped<IncidentInvestigationLauncher>();

        // Deployment assurance
        builder.Services.AddSingleton<ApprovalAgingPolicy>();
        builder.Services.AddSingleton<PipelineFailureClassifier>();
        builder.Services.AddSingleton<RuntimeDriftService>();
        builder.Services.AddSingleton<DeploymentValidationService>();

        // API Client — variable substitution and HTTP execution
        builder.Services.AddSingleton<IVariableSubstitutionService, VariableSubstitutionService>();
        builder.Services.AddSingleton<IVariablePreviewService, VariablePreviewService>();
        builder.Services.AddSingleton<IPostRequestCaptureExecutor, PostRequestCaptureExecutor>();
        builder.Services.AddSingleton<IKeyVaultSecretResolver>(sp =>
        {
            var config = sp.GetRequiredService<AppStateService>().Config;
            return string.IsNullOrWhiteSpace(config.KeyVaultUrl)
                ? new NoopKeyVaultSecretResolver()
                : new AzureKeyVaultSecretResolver(
                    config.KeyVaultUrl,
                    sp.GetRequiredService<ILogger<AzureKeyVaultSecretResolver>>());
        });
        builder.Services.AddTransient<IHttpRequestExecutor, HttpRequestExecutor>();
        builder.Services.AddHttpClient(HttpRequestExecutor.ClientName)
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var settings = sp.GetRequiredService<UserSettingsRepository>().Settings;
                return new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    ServerCertificateCustomValidationCallback =
                        settings.VerifyApiClientSsl
                            ? null
                            : (_, _, _, _) => true,
                };
            });

        // Connection warmup
        builder.Services.AddSingleton<IAksWarmupCache, AksWarmupCache>();
        builder.Services.AddSingleton<IRedisWarmupCache, RedisWarmupCache>();
        builder.Services.AddSingleton<IServiceBusWarmupCache, ServiceBusWarmupCache>();
        builder.Services.AddSingleton<IConnectionWarmupService, ConnectionWarmupService>();
        builder.Services.AddSingleton<RedisOpsInsightsAggregator>();

        var app = builder.Build();
        PerformanceBaselineRecorder.Record(nameof(MauiProgram), "Perf startup CreateMauiApp completed");
        return app;
    }
}
