using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Azure.Observability;

public class AppInsightsObservabilityProvider : IObservabilityProvider
{
    private readonly LogsQueryClient _logsClient;
    private readonly MetricsQueryClient _metricsClient;
    private readonly ObservabilityConfig _config;

    public ObservabilityProviderType ProviderType => ObservabilityProviderType.AppInsights;
    public bool IsConnected { get; private set; }

    public AppInsightsObservabilityProvider(ObservabilityConfig config, ICredentialStore credentialStore)
    {
        _config = config;
        var credential = new DefaultAzureCredential();
        _logsClient = new LogsQueryClient(credential);
        _metricsClient = new MetricsQueryClient(credential);
    }

    public async Task<IReadOnlyList<LogEntry>> QueryLogsAsync(LogQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_config.WorkspaceId))
            throw new InvalidOperationException("WorkspaceId is not configured.");

        var kql = query.RawKql ?? BuildKql(query);
        var range = ParseTimeRange(query.TimeRange);

        var response = await _logsClient.QueryWorkspaceAsync(
            _config.WorkspaceId, kql, range, cancellationToken: ct);

        return MapLogRows(response.Value);
    }

    public async Task<TraceTimeline?> GetTraceAsync(string operationId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_config.WorkspaceId)) return null;

        var kql = $"""
            let opId = '{operationId.Replace("'", "''")}';
            union requests, dependencies, exceptions, traces
            | where operation_Id == opId
            | order by timestamp asc
            | take 500
            """;

        var response = await _logsClient.QueryWorkspaceAsync(
            _config.WorkspaceId, kql, QueryTimeRange.All, cancellationToken: ct);

        return MapTrace(operationId, response.Value);
    }

    public async Task<IReadOnlyList<MetricSeries>> GetMetricsAsync(MetricsQuery query, CancellationToken ct = default)
    {
        // Metrics queries require resource ID - minimal implementation
        return [];
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(_config.WorkspaceId)) return false;
            await _logsClient.QueryWorkspaceAsync(_config.WorkspaceId, "union * | take 1", QueryTimeRange.All, cancellationToken: ct);
            IsConnected = true;
            return true;
        }
        catch
        {
            IsConnected = false;
            return false;
        }
    }

    private static string BuildKql(LogQuery query)
    {
        var parts = new List<string> { "union traces, exceptions" };

        var timeFilter = $"timestamp > ago({query.TimeRange})";
        parts.Add($"| where {timeFilter}");

        if (query.Levels.Count > 0)
        {
            var levels = string.Join(", ", query.Levels.Select(l => (int)l));
            parts.Add($"| where severityLevel in ({levels})");
        }

        if (!string.IsNullOrEmpty(query.CorrelationId))
            parts.Add($"| where operation_Id == '{query.CorrelationId.Replace("'", "''")}'");

        if (!string.IsNullOrEmpty(query.TextSearch))
            parts.Add($"| where message contains '{query.TextSearch.Replace("'", "''")}'");

        if (!string.IsNullOrEmpty(query.OperationName))
            parts.Add($"| where operation_Name contains '{query.OperationName.Replace("'", "''")}'");

        parts.Add("| order by timestamp desc");
        parts.Add($"| take {query.MaxRows}");

        return string.Join("\n", parts);
    }

    private static IReadOnlyList<LogEntry> MapLogRows(LogsQueryResult result)
    {
        var table = result.Table;
        var entries = new List<LogEntry>();

        foreach (var row in table.Rows)
        {
            entries.Add(new LogEntry
            {
                Timestamp = row.GetDateTimeOffset("timestamp") ?? DateTimeOffset.UtcNow,
                Level = ParseLevel(row.GetString("severityLevel")),
                Message = row.GetString("message") ?? row.GetString("outerMessage") ?? string.Empty,
                OperationName = row.GetString("operation_Name"),
                OperationId = row.GetString("operation_Id"),
                CorrelationId = row.GetString("operation_Id"),
                SourceProvider = ObservabilityProviderType.AppInsights
            });
        }

        return entries;
    }

    private static TraceTimeline? MapTrace(string operationId, LogsQueryResult result)
    {
        if (result.Table.Rows.Count == 0) return null;

        var spans = new List<TraceSpan>();
        DateTimeOffset? earliest = null;

        foreach (var row in result.Table.Rows)
        {
            var ts = row.GetDateTimeOffset("timestamp") ?? DateTimeOffset.UtcNow;
            if (earliest is null || ts < earliest) earliest = ts;

            var duration = row.GetDouble("duration") is double d ? TimeSpan.FromMilliseconds(d) : TimeSpan.Zero;
            var itemType = row.GetString("itemType") ?? "trace";

            spans.Add(new TraceSpan
            {
                SpanId = row.GetString("id") ?? Guid.NewGuid().ToString(),
                ParentSpanId = null,
                Name = row.GetString("name") ?? row.GetString("message") ?? itemType,
                Kind = itemType switch { "request" => SpanKind.Server, "dependency" => SpanKind.Client, _ => SpanKind.Internal },
                StartTime = ts,
                Duration = duration,
                Status = row.GetBoolean("success") == false ? SpanStatus.Error : SpanStatus.Ok
            });
        }

        var totalDuration = spans.Count > 0
            ? spans.Max(s => s.StartTime + s.Duration) - earliest!.Value
            : TimeSpan.Zero;

        return new TraceTimeline
        {
            RootOperationId = operationId,
            StartTime = earliest ?? DateTimeOffset.UtcNow,
            TotalDuration = totalDuration,
            Spans = [.. spans.OrderBy(s => s.StartTime)]
        };
    }

    private static LogLevel ParseLevel(string? level) => level switch
    {
        "0" => LogLevel.Trace,
        "1" => LogLevel.Information,
        "2" => LogLevel.Warning,
        "3" => LogLevel.Error,
        "4" => LogLevel.Critical,
        _ => LogLevel.Information
    };

    private static QueryTimeRange ParseTimeRange(string range) => range switch
    {
        "15m" => new QueryTimeRange(TimeSpan.FromMinutes(15)),
        "1h" => new QueryTimeRange(TimeSpan.FromHours(1)),
        "6h" => new QueryTimeRange(TimeSpan.FromHours(6)),
        "24h" => new QueryTimeRange(TimeSpan.FromHours(24)),
        _ => new QueryTimeRange(TimeSpan.FromHours(1))
    };
}
