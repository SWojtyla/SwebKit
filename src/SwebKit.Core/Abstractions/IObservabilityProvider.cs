using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IObservabilityProvider
{
    ObservabilityProviderType ProviderType { get; }
    bool IsConnected { get; }

    Task<IReadOnlyList<LogEntry>> QueryLogsAsync(LogQuery query, CancellationToken ct = default);
    Task<TraceTimeline?> GetTraceAsync(string operationId, CancellationToken ct = default);
    Task<IReadOnlyList<MetricSeries>> GetMetricsAsync(MetricsQuery query, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
