using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwebKit.App.Platforms.Windows;
using SwebKit.App.Services;
using SwebKit.Azure.ServiceBus;
using SwebKit.Azure.Storage;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Diagnostics;
using SwebKit.Core.Services;
using SwebKit.Kubernetes.AksClient;
using SwebKit.Observability;
using SwebKit.Redis;

namespace SwebKit.App.Hosting;

/// <summary>
/// Extension methods for registering SwebKit core infrastructure services.
/// </summary>
public static partial class SwebKitServiceCollectionExtensions
{
    /// <summary>
    /// Registers core infrastructure: credential store, event bus, task queue,
    /// repositories, configuration services, and platform-specific services.
    /// </summary>
    public static IServiceCollection AddSwebKitCore(this IServiceCollection services, UserSettingsRepository userSettingsRepository)
    {
        services.AddSingleton<ICredentialStore, Platforms.Windows.WindowsCredentialStore>();
        services.AddSingleton<IAppEventBus, AppEventBus>();
        services.AddSingleton<ITaskQueue, TaskQueueService>();
        services.AddSingleton<ProfileRepository>();
        services.AddSingleton<UiStateRepository>();
        services.AddSingleton(userSettingsRepository);
        services.AddSingleton<ILogRetentionCleanupService>(_ => new LogRetentionCleanupService(AppDataPaths.LogsDirectory));
        services.AddSingleton<PinnedPortForwardService>();
        services.AddSingleton<ScheduledMessageRepository>();
        services.AddSingleton<AppStateService>();
        services.AddSingleton<ConfigurationBundleService>();
        services.AddSingleton<CollectionRepository>();
        services.AddSingleton<EnvironmentRepository>();
        services.AddSingleton<LinkedCollectionRootRepository>();
        services.AddSingleton<IConfigurationHealthService, ConfigurationHealthService>();
        services.AddSingleton<IConfigurationProbeService, ConfigurationProbeService>();

        return services;
    }

    /// <summary>
    /// Registers app-level UI services: tabs, commands, notifications, bootstrappers,
    /// factories, search providers, and platform-specific tray/window services.
    /// </summary>
    public static IServiceCollection AddSwebKitAppServices(this IServiceCollection services)
    {
        services.AddSingleton<IConnectionStateService, ConnectionStateService>();
        services.AddSingleton<TabService>();
        services.AddSingleton<CommandRegistry>();
        services.AddScoped<OperatorWorkspaceService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IToastDiagnosticService, ToastDiagnosticService>();
        services.AddSingleton<IAksClientBootstrapper, AksClientBootstrapper>();
        services.AddSingleton<IShellErrorPresenter, ShellErrorPresenter>();
        services.AddSingleton<IPortForwardSessionService, PortForwardSessionService>();
        services.AddSingleton<ISelectionContext, SelectionContext>();
        services.AddSingleton<IServiceBusNamespaceBootstrapper, ServiceBusNamespaceBootstrapper>();
        services.AddSingleton<IServiceBusClientFactory, ServiceBusClientFactory>();
        services.AddSingleton<IAksClientFactory, AksClientFactory>();
        services.AddSingleton<IStorageClientFactory, StorageClientFactory>();
        services.AddSingleton<IRedisClientFactory, RedisClientFactory>();
        services.AddSingleton<IOperatorResourceSearchProvider, ServiceBusResourceSearchProvider>();
        services.AddSingleton<IOperatorResourceSearchProvider, AksResourceSearchProvider>();
        services.AddSingleton<IOperatorResourceSearchProvider, StorageResourceSearchProvider>();
        services.AddSingleton<IOperatorResourceSearchProvider, RedisResourceSearchProvider>();
        services.AddSingleton<IOperatorResourceSearchProvider, ObservabilityResourceSearchProvider>();
        services.AddSingleton<IOperatorResourceSearchProvider, IncidentTimelineSearchProvider>();
        services.AddSingleton<TrayLifecycleState>();

#if WINDOWS
        services.AddSingleton<IWindowsNotificationService, WindowsToastNotificationService>();
        services.AddSingleton<ITrayLifecycleService, WindowsTrayLifecycleService>();
        services.AddSingleton<IFolderPickerService, WindowsFolderPickerService>();
#else
        services.AddSingleton<IWindowsNotificationService, NullWindowsNotificationService>();
        services.AddSingleton<ITrayLifecycleService, NullTrayLifecycleService>();
#endif

        return services;
    }
}
