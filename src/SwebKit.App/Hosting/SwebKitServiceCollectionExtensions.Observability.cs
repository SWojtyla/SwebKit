using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Services;
using SwebKit.Azure.ServiceBus.IncidentTimeline;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;
using SwebKit.DevOps.IncidentTimeline;
using SwebKit.Kubernetes.IncidentTimeline;
using SwebKit.Observability;
using SwebKit.Observability.IncidentTimeline;

namespace SwebKit.App.Hosting;

/// <summary>
/// Extension methods for observability and incident timeline services.
/// </summary>
public static partial class SwebKitServiceCollectionExtensions
{
    /// <summary>
    /// Registers observability resource discovery, provider factory, KQL compiler,
    /// and explainer service.
    /// </summary>
    public static IServiceCollection AddSwebKitObservability(this IServiceCollection services)
    {
        services.AddSingleton<IObservabilityResourceDiscovery, AppInsightsDiscoveryService>();
        services.AddSingleton<IObservabilityProviderFactory, ObservabilityProviderFactory>();
        services.AddSingleton<IGuidedKqlCompiler, GuidedKqlCompiler>();
        services.AddSingleton<IObservabilityExplainerService, ObservabilityExplainerService>();
        return services;
    }

    /// <summary>
    /// Registers incident timeline signal sources, investigation services,
    /// snapshot exporter, and mapping proposal generator.
    /// </summary>
    public static IServiceCollection AddSwebKitIncidentTimeline(this IServiceCollection services)
    {
        services.AddSingleton<IIncidentTimelineSignalSource, AksTimelineSignalSource>();
        services.AddSingleton<IIncidentTimelineSignalSource, AppInsightsTimelineSignalSource>();
        services.AddSingleton<IIncidentTimelineSignalSource, ServiceBusEvidenceSignalSource>();
        services.AddSingleton<IIncidentTimelineSignalSource, DevOpsReleaseTimelineSignalSource>();
        services.AddSingleton<IIncidentTimelineService, IncidentTimelineService>();
        services.AddSingleton<IIncidentInvestigationSeedResolver, IncidentInvestigationSeedResolver>();
        services.AddSingleton<IIncidentSnapshotExporter, IncidentSnapshotExporter>();
        services.AddSingleton<IIncidentMappingProposalGenerator, IncidentMappingProposalGenerator>();
        services.AddScoped<IncidentInvestigationLauncher>();
        return services;
    }
}
