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

    Task<IReadOnlyList<LatencyDataPoint>> GetOperationLatencyTrendAsync(
        string operationName, TimeRange range, CancellationToken ct = default);

    /// <summary>Returns provider-specific preset queries shown in the Logs tab sidebar.</summary>
    IReadOnlyList<QueryPreset> GetPresets();

    Task<DependencyHealthSummary> GetDependencyHealthAsync(TimeRange range, int maxDependencies = 20, CancellationToken ct = default);

    Task<DimensionBreakdown> GetDimensionBreakdownAsync(TimeRange range, string dimensionKey, int topN = 15, CancellationToken ct = default);
}

/// <summary>
/// Discovers available observability resources (e.g. App Insights components across Azure subscriptions).
/// This is Azure-specific; self-hosted OTLP backends don't need resource discovery.
/// </summary>
public interface IObservabilityResourceDiscovery
{
    IAsyncEnumerable<ObservabilityResourceInfo> DiscoverResourcesAsync(CancellationToken ct = default);
    void InvalidateCache() { }
}

public interface IGuidedKqlCompiler
{
    GuidedKqlCompileResult Compile(GuidedKqlQueryDefinition definition);
}

/// <summary>One Log Analytics workspace as discovered through ARM — carries the
/// <c>customerId</c> the Logs query API keys on (<c>QueryWorkspaceAsync</c> takes the
/// workspace customerId GUID, not the ARM resource id).</summary>
public record LogAnalyticsWorkspaceInfo(
    string ResourceId,
    string Name,
    string CustomerId,
    string SubscriptionId,
    string SubscriptionName,
    string ResourceGroup,
    string Location);

/// <summary>
/// Finds Log Analytics workspaces and runs KQL against them — the workspace analogue of the
/// <see cref="IObservabilityResourceDiscovery"/> + <see cref="IObservabilityProvider"/> pair
/// for App Insights. Kept separate because the query surface differs (workspace customerId vs.
/// resource id) and a workspace carries no overview/metrics/presets concept.
/// </summary>
public interface ILogAnalyticsWorkspaceService
{
    /// <summary>Workspaces visible to the signed-in identity across subscriptions, optionally
    /// narrowed by a case-insensitive substring matched against the workspace name and its ARM
    /// resource id.</summary>
    Task<IReadOnlyList<LogAnalyticsWorkspaceInfo>> FindWorkspacesAsync(string? nameFilter = null, CancellationToken ct = default);

    /// <summary>Runs KQL against a workspace by its <c>customerId</c>.</summary>
    Task<LogQueryResult> RunWorkspaceQueryAsync(string customerId, string query, TimeRange range, int maxRows, CancellationToken ct = default);
}
