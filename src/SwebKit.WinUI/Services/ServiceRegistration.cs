using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using SwebKit.Azure.ServiceBus;
using SwebKit.Azure.ServiceBus.IncidentTimeline;
using SwebKit.Azure.Storage;
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
using SwebKit.WinUI.Platforms.Windows;
using SwebKit.WinUI.ViewModels.ServiceBus;
using SwebKit.WinUI.ViewModels.Settings;
using SwebKit.WinUI.ViewModels.Shell;

namespace SwebKit.WinUI.Services;

/// <summary>
/// Registers all DI services for the WinUI host.
/// Mirrors MauiProgram.cs — MAUI and Blazor-specific registrations are omitted.
/// </summary>
internal static class ServiceRegistration
{
    internal static void Register(IServiceCollection services)
    {
        // ── Windows platform ──────────────────────────────────────────────────────
        services.AddSingleton<ICredentialStore, WindowsCredentialStore>();
        services.AddSingleton<IWindowsNotificationService, WindowsToastNotificationService>();
        services.AddSingleton<ITrayLifecycleService, WindowsTrayLifecycleService>();

        // ── Core infrastructure ───────────────────────────────────────────────────
        services.AddSingleton<IAppEventBus, AppEventBus>();
        services.AddSingleton<ITaskQueue, TaskQueueService>();
        services.AddSingleton<ProfileRepository>();
        services.AddSingleton<UiStateRepository>();
        services.AddSingleton<UserSettingsRepository>();
        services.AddSingleton<ScheduledMessageRepository>();
        services.AddSingleton<AppStateService>();
        services.AddSingleton<IConnectionStateService, ConnectionStateService>();
        services.AddSingleton<IPortForwardSessionService, PortForwardSessionService>();

        // ── Demo clients ──────────────────────────────────────────────────────────
        services.AddSingleton<DemoAksClient>();
        services.AddSingleton(new DemoRedisClient(0));
        services.AddSingleton<DemoStorageClient>();
        services.AddSingleton<DemoDevOpsClient>();

        // ── Azure integration ─────────────────────────────────────────────────────
        services.AddSingleton<IServiceBusNamespaceBootstrapper, ServiceBusNamespaceBootstrapper>();
        services.AddSingleton<IServiceBusClientFactory, ServiceBusClientFactory>();
        services.AddSingleton<IStorageClientFactory, StorageClientFactory>();

        // ── Kubernetes ────────────────────────────────────────────────────────────
        services.AddSingleton<IAksClientFactory, AksClientFactory>();

        // ── Redis ─────────────────────────────────────────────────────────────────
        services.AddSingleton<IRedisClientFactory, RedisClientFactory>();

        // ── Observability ─────────────────────────────────────────────────────────
        services.AddSingleton<IObservabilityResourceDiscovery, AppInsightsDiscoveryService>();
        services.AddSingleton<IGuidedKqlCompiler, GuidedKqlCompiler>();
        services.AddSingleton<IObservabilityExplainerService, ObservabilityExplainerService>();

        // ── DevOps / Releases ─────────────────────────────────────────────────────
        services.AddTransient<DevOpsAuthHandler>();
        services.AddHttpClient("AzureDevOps")
            .AddHttpMessageHandler<DevOpsAuthHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromSeconds(1);
            });
        services.AddSingleton<IDevOpsClientFactory, DevOpsClientFactory>();
        services.AddSingleton<ReleaseRepository>();

        // ── Incident timeline ─────────────────────────────────────────────────────
        services.AddSingleton<IIncidentTimelineSignalSource, AksTimelineSignalSource>();
        services.AddSingleton<IIncidentTimelineSignalSource, AppInsightsTimelineSignalSource>();
        services.AddSingleton<IIncidentTimelineSignalSource, ServiceBusEvidenceSignalSource>();
        services.AddSingleton<IIncidentTimelineSignalSource, DevOpsReleaseTimelineSignalSource>();
        services.AddSingleton<IIncidentTimelineService, IncidentTimelineService>();
        services.AddSingleton<IIncidentInvestigationSeedResolver, IncidentInvestigationSeedResolver>();
        services.AddSingleton<IIncidentSnapshotExporter, IncidentSnapshotExporter>();
        services.AddSingleton<IIncidentMappingProposalGenerator, IncidentMappingProposalGenerator>();

        // ── Deployment assurance ──────────────────────────────────────────────────
        services.AddSingleton<ApprovalAgingPolicy>();
        services.AddSingleton<PipelineFailureClassifier>();
        services.AddSingleton<RuntimeDriftService>();
        services.AddSingleton<DeploymentValidationService>();

        // ── Phase 1: Shell services ───────────────────────────────────────────────
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IShellErrorPresenter, ShellErrorPresenter>();
        services.AddSingleton<TabService>();
        services.AddSingleton<CommandRegistry>();

        // Resource search providers for command palette / workspace search
        services.AddSingleton<IOperatorResourceSearchProvider, ServiceBusResourceSearchProvider>();
        services.AddSingleton<IOperatorResourceSearchProvider, AksResourceSearchProvider>();
        services.AddSingleton<IOperatorResourceSearchProvider, StorageResourceSearchProvider>();
        services.AddSingleton<IOperatorResourceSearchProvider, RedisResourceSearchProvider>();
        services.AddSingleton<IOperatorResourceSearchProvider, ObservabilityResourceSearchProvider>();
        services.AddSingleton<IOperatorResourceSearchProvider, IncidentTimelineSearchProvider>();

        // MainWindowViewModel implements IShellNavigationService — register as both
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<IShellNavigationService>(sp => sp.GetRequiredService<MainWindowViewModel>());

        services.AddSingleton<OperatorWorkspaceService>();

        // ── Phase 1: ViewModels ───────────────────────────────────────────────────
        services.AddSingleton<CommandPaletteViewModel>();
        services.AddTransient<ServiceBusPageViewModel>();
        services.AddTransient<SettingsViewModel>();

        // ── Shell window ──────────────────────────────────────────────────────────
        services.AddSingleton<MainWindow>();
    }
}
