using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IObservabilityExplainerService
{
    Task<ObservabilityExplainerSummary> GetExplainerSummaryAsync(
        IObservabilityProvider provider,
        TimeRange range,
        IReadOnlyList<string> dimensionKeys,
        CancellationToken ct = default);

    Task<DeploymentComparisonSummary> GetDeploymentComparisonAsync(
        IObservabilityProvider provider,
        DeploymentAnchor anchor,
        TimeSpan windowDuration,
        CancellationToken ct = default);

    Task<SloStatusSummary> GetSloStatusAsync(
        IObservabilityProvider provider,
        IReadOnlyList<SloDefinition> definitions,
        TimeRange range,
        CancellationToken ct = default);
}
