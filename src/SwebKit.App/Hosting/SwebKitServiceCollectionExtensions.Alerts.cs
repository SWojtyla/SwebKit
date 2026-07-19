using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Services;
using SwebKit.Azure.ServiceBus;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;
using SwebKit.Kubernetes.AksClient;
using SwebKit.Redis;

namespace SwebKit.App.Hosting;

/// <summary>
/// Extension methods for alert monitoring and demo client registration.
/// </summary>
public static partial class SwebKitServiceCollectionExtensions
{
    /// <summary>
    /// Registers the alert monitor system and all alert signal sources.
    /// </summary>
    public static IServiceCollection AddSwebKitAlerts(this IServiceCollection services)
    {
        services.AddSingleton<IMonitoringConnectionPool, MonitoringConnectionPool>();
        services.AddSingleton<IAlertRuleRepository, AlertRuleRepository>();
        services.AddSingleton<IAlertSignalSource, AksPodHealthSignalSource>();
        services.AddSingleton<IAlertSignalSource, AksPodRestartRateSignalSource>();
        services.AddSingleton<IAlertSignalSource, AksNamespaceHealthScoreSignalSource>();
        services.AddSingleton<IAlertSignalSource, ServiceBusDlqSignalSource>();
        services.AddSingleton<IAlertSignalSource, ServiceBusActiveDepthSignalSource>();
        services.AddSingleton<IAlertSignalSource, ServiceBusDeadSubscriptionSignalSource>();
        services.AddSingleton<IAlertSignalSource, RedisMemorySignalSource>();
        services.AddSingleton<IAlertSignalSource, RedisConnectedClientsSignalSource>();
        services.AddSingleton<IAlertMonitorService, AlertMonitorService>();
        services.AddSingleton<MonitoringMigrationService>();
        // Null stub retains DashboardPage + legacy AKS sub-component DI compatibility
        services.AddSingleton<IPodHealthMonitorService, NullPodHealthMonitorService>();

        return services;
    }

    /// <summary>
    /// Registers demo clients as singletons. Pages select real vs. demo based on
    /// AppStateService.UseDemoData.
    /// </summary>
    public static IServiceCollection AddSwebKitDemoClients(this IServiceCollection services)
    {
        services.AddSingleton<DemoAksClient>();
        services.AddSingleton(new DemoRedisClient(0));
        services.AddSingleton<DemoStorageClient>();
        services.AddSingleton<DemoDevOpsClient>();
        return services;
    }
}
