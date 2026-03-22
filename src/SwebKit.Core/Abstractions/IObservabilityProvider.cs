using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Backend-agnostic observability provider.
/// Current implementations: AzureAppInsightsProvider (Azure Monitor Logs API).
/// Future: OtlpObservabilityProvider (Prometheus/OTLP backends).
/// </summary>
public interface IObservabilityProvider
{
    /// <summary>Human-readable backend type, e.g. "Azure Application Insights", "OpenTelemetry".</summary>
    string ProviderType { get; }

    Task<OverviewMetrics> GetOverviewAsync(TimeRange range, CancellationToken ct = default);

    Task<IReadOnlyList<ExceptionGroup>> GetTopExceptionsAsync(TimeRange range, int top = 20, CancellationToken ct = default);

    /// <summary>Returns individual occurrences of a specific exception type for detail pane.</summary>
    Task<IReadOnlyList<LogRow>> GetExceptionSamplesAsync(string exceptionType, TimeRange range, CancellationToken ct = default);

    Task<IReadOnlyList<OperationPerformance>> GetOperationPerformanceAsync(TimeRange range, CancellationToken ct = default);

    /// <summary>
    /// Runs a free-form query in the provider's native query language (KQL for App Insights, PromQL for OTLP).
    /// </summary>
    Task<LogQueryResult> RunQueryAsync(string query, TimeRange range, int maxRows = 500, CancellationToken ct = default);

    Task<IReadOnlyList<AvailabilityResult>> GetAvailabilityAsync(TimeRange range, CancellationToken ct = default);

    /// <summary>Returns provider-specific preset queries shown in the Logs tab sidebar.</summary>
    IReadOnlyList<QueryPreset> GetPresets();
}

/// <summary>
/// Discovers available observability resources (e.g. App Insights components across Azure subscriptions).
/// This is Azure-specific; self-hosted OTLP backends don't need resource discovery.
/// </summary>
public interface IObservabilityResourceDiscovery
{
    IAsyncEnumerable<ObservabilityResourceInfo> DiscoverResourcesAsync(CancellationToken ct = default);
}
