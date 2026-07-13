using Azure;
using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Observability;

/// <summary>
/// Azure Application Insights implementation of IObservabilityProvider.
/// Queries Azure Monitor Logs API via LogsQueryClient, scoped to a single App Insights resource.
/// Authentication: DefaultAzureCredential (Azure CLI, VS, Managed Identity, etc.)
/// </summary>
public sealed class AzureAppInsightsProvider : IObservabilityProvider
{
    private readonly LogsQueryClient _client;
    private readonly ResourceIdentifier _resourceId;

    public string ProviderType => "Azure Application Insights";

    public AzureAppInsightsProvider(string resourceId)
    {
        _resourceId = new ResourceIdentifier(resourceId);
        // See AzureCredentialFactory for why EnvironmentCredential is excluded.
        _client = new LogsQueryClient(AzureCredentialFactory.CreateDefault());
    }

    // ── Overview ──────────────────────────────────────────────────────────────

    public async Task<OverviewMetrics> GetOverviewAsync(TimeRange range, CancellationToken ct = default)
    {
        var qr = QueryTimeRange(range);

        // Run summary and trend queries in parallel
        var summaryTask = QuerySingleRowAsync(
            "requests\n| summarize RequestCount=count(), FailedCount=countif(success==false), P50=percentile(duration,50), P95=percentile(duration,95)",
            qr, ct);

        var exCountTask = QuerySingleValueAsync<long>(
            "exceptions | count",
            qr, ct);

        var availTask = QuerySingleValueAsync<double>(
            "availabilityResults | summarize avg(todouble(success))*100",
            qr, ct);

        var requestTrendTask = QuerySeriesAsync(
            "requests | summarize Value=count() by bin(timestamp,1h) | order by timestamp asc",
            qr, ct);

        var failureTrendTask = QuerySeriesAsync(
            "requests | summarize _fail=countif(success==false), _total=count() by bin(timestamp,1h) | extend Value=todouble(_fail)/todouble(max_of(1, _total)) | order by timestamp asc",
            qr, ct);

        await Task.WhenAll(summaryTask, exCountTask, availTask, requestTrendTask, failureTrendTask).ConfigureAwait(false);

        var summary = await summaryTask.ConfigureAwait(false);
        long requestCount = summary is not null ? GetLong(summary, "RequestCount") : 0;
        long failedCount = summary is not null ? GetLong(summary, "FailedCount") : 0;
        double p50 = summary is not null ? GetDouble(summary, "P50") : 0;
        double p95 = summary is not null ? GetDouble(summary, "P95") : 0;
        double failureRate = requestCount > 0 ? (double)failedCount / requestCount : 0;

        return new OverviewMetrics(
            RequestCount: requestCount,
            FailureRate: failureRate,
            P50ResponseTimeMs: p50,
            P95ResponseTimeMs: p95,
            ExceptionCount: await exCountTask.ConfigureAwait(false),
            AvailabilityPct: await availTask.ConfigureAwait(false),
            RequestTrend: await requestTrendTask.ConfigureAwait(false),
            FailureTrend: await failureTrendTask.ConfigureAwait(false));
    }

    // ── Failures ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ExceptionGroup>> GetTopExceptionsAsync(TimeRange range, int top = 20, CancellationToken ct = default)
    {
        var kql = $"exceptions | summarize Count=count(), LastSeen=max(timestamp), SampleMessage=any(innermostMessage), SampleStack=any(details[0].rawStack) by type, problemId | order by Count desc | take {top}";
        var result = await QueryTableAsync(kql, QueryTimeRange(range), ct).ConfigureAwait(false);

        return result.Select(row => new ExceptionGroup(
            ExceptionType: GetString(row, "type"),
            ProblemId: GetString(row, "problemId"),
            Count: GetLong(row, "Count"),
            LastSeen: GetDateTimeOffset(row, "LastSeen"),
            SampleMessage: TryGetString(row, "SampleMessage"),
            SampleStackTrace: TryGetString(row, "SampleStack")
        )).ToList();
    }

    public async Task<IReadOnlyList<LogRow>> GetExceptionSamplesAsync(string exceptionType, TimeRange range, CancellationToken ct = default)
    {
        var escapedType = exceptionType.Replace("'", "\\'");
        var kql = $"exceptions | where type == '{escapedType}' | project timestamp, type, operationId=operation_Id, operationName=operation_Name, cloud_RoleName, severityLevel | order by timestamp desc | take 20";
        var rows = await QueryTableAsync(kql, QueryTimeRange(range), ct).ConfigureAwait(false);
        return rows.Select(MapLogRow).ToList();
    }

    // ── Performance ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<OperationPerformance>> GetOperationPerformanceAsync(TimeRange range, CancellationToken ct = default)
    {
        var kql = "requests | summarize Count=count(), FailedCount=countif(success==false), P50=percentile(duration,50), P95=percentile(duration,95), P99=percentile(duration,99) by name | order by P95 desc | take 50";
        var result = await QueryTableAsync(kql, QueryTimeRange(range), ct).ConfigureAwait(false);

        return result.Select(row =>
        {
            var count = GetLong(row, "Count");
            var failed = GetLong(row, "FailedCount");
            return new OperationPerformance(
                OperationName: GetString(row, "name"),
                RequestCount: count,
                FailureRate: count > 0 ? (double)failed / count : 0,
                P50Ms: GetDouble(row, "P50"),
                P95Ms: GetDouble(row, "P95"),
                P99Ms: GetDouble(row, "P99"));
        }).ToList();
    }

    // ── Logs ──────────────────────────────────────────────────────────────────

    public async Task<LogQueryResult> RunQueryAsync(string query, TimeRange range, int maxRows = 500, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var options = new LogsQueryOptions { ServerTimeout = TimeSpan.FromSeconds(60) };
            var response = await _client.QueryResourceAsync(
                _resourceId,
                query,
                QueryTimeRange(range),
                options,
                ct).ConfigureAwait(false);

            sw.Stop();

            if (!response.HasValue)
                return EmptyResult(sw.Elapsed);

            var table = response.Value.Table;
            var columns = table.Columns.Select(c => c.Name).ToList();
            return LogQueryResultProjector.Project(
                columns,
                table.Rows,
                static (row, index) => row[index],
                sw.Elapsed,
                maxRows);
        }
        catch (RequestFailedException ex)
        {
            sw.Stop();
            throw new InvalidOperationException($"Azure Monitor query failed ({ex.Status}): {ex.Message}", ex);
        }
    }

    // ── Availability ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AvailabilityResult>> GetAvailabilityAsync(TimeRange range, CancellationToken ct = default)
    {
        var kql = "availabilityResults | project timestamp, name, location, success, duration, message | order by timestamp desc | take 200";
        var rows = await QueryTableAsync(kql, QueryTimeRange(range), ct).ConfigureAwait(false);

        return rows.Select(row => new AvailabilityResult(
            TestName: GetString(row, "name"),
            Location: GetString(row, "location"),
            Success: GetBool(row, "success"),
            Timestamp: GetDateTimeOffset(row, "timestamp"),
            DurationMs: GetDouble(row, "duration"),
            FailureMessage: TryGetString(row, "message")
        )).ToList();
    }

    // ── Presets ───────────────────────────────────────────────────────────────

    public IReadOnlyList<QueryPreset> GetPresets() => KqlPresets.All;

    // ── Latency trend ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<LatencyDataPoint>> GetOperationLatencyTrendAsync(
        string operationName, TimeRange range, CancellationToken ct = default)
    {
        var binSize = DeriveBinSize(range);
        var escapedName = operationName.Replace("'", "\\'");
        var kql = $"requests\n| where name == '{escapedName}'\n| summarize P50=percentile(duration, 50), P95=percentile(duration, 95), P99=percentile(duration, 99) by bin(timestamp, {binSize})\n| order by timestamp asc";

        try
        {
            var rows = await QueryTableAsync(kql, QueryTimeRange(range), ct).ConfigureAwait(false);
            return rows.Select(row => new LatencyDataPoint(
                Timestamp: GetDateTimeOffset(row, "timestamp"),
                P50Ms: GetDouble(row, "P50"),
                P95Ms: GetDouble(row, "P95"),
                P99Ms: GetDouble(row, "P99")
            )).ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch (RequestFailedException ex)
        {
            throw new InvalidOperationException($"Latency trend query failed ({ex.Status}): {ex.Message}", ex);
        }
    }

    // ── Dependency health ─────────────────────────────────────────────────────

    public async Task<DependencyHealthSummary> GetDependencyHealthAsync(TimeRange range, int maxDependencies = 20, CancellationToken ct = default)
    {
        var kql = $"dependencies\n| summarize CallCount=count(), FailedCount=countif(success==false), P50=percentile(duration,50), P95=percentile(duration,95) by name, type\n| order by CallCount desc\n| take {maxDependencies + 1}";

        try
        {
            var rows = await QueryTableAsync(kql, QueryTimeRange(range), ct).ConfigureAwait(false);
            bool truncated = rows.Count > maxDependencies;
            var entries = rows.Take(maxDependencies).Select(row =>
            {
                var callCount = GetLong(row, "CallCount");
                var failedCount = GetLong(row, "FailedCount");
                return new DependencyHealthEntry(
                    DependencyName: GetString(row, "name"),
                    DependencyType: GetString(row, "type"),
                    CallCount: callCount,
                    FailureRate: callCount > 0 ? (double)failedCount / callCount : 0,
                    P50Ms: GetDouble(row, "P50"),
                    P95Ms: GetDouble(row, "P95"));
            }).ToList();
            return new DependencyHealthSummary(entries, truncated, maxDependencies);
        }
        catch (OperationCanceledException) { throw; }
        catch (RequestFailedException ex)
        {
            throw new InvalidOperationException($"Dependency health query failed ({ex.Status}): {ex.Message}", ex);
        }
    }

    // ── Dimension breakdown ───────────────────────────────────────────────────

    public async Task<DimensionBreakdown> GetDimensionBreakdownAsync(TimeRange range, string dimensionKey, int topN = 15, CancellationToken ct = default)
    {
        var escapedKey = dimensionKey.Replace("'", "\\'");
        var kql = $"requests\n| where isnotempty(customDimensions['{escapedKey}'])\n| summarize Count=count(), FailedCount=countif(success==false) by Value=tostring(customDimensions['{escapedKey}'])\n| order by Count desc\n| take {topN + 1}";

        try
        {
            var rows = await QueryTableAsync(kql, QueryTimeRange(range), ct).ConfigureAwait(false);
            bool truncated = rows.Count > topN;
            var entries = rows.Take(topN).Select(row =>
            {
                var count = GetLong(row, "Count");
                var failedCount = GetLong(row, "FailedCount");
                return new DimensionBreakdownEntry(
                    Value: GetString(row, "Value"),
                    Count: count,
                    FailureRate: count > 0 ? (double)failedCount / count : 0);
            }).ToList();
            return new DimensionBreakdown(dimensionKey, entries, truncated, topN);
        }
        catch (OperationCanceledException) { throw; }
        catch (RequestFailedException ex)
        {
            throw new InvalidOperationException($"Dimension breakdown query failed ({ex.Status}): {ex.Message}", ex);
        }
    }

    private static string DeriveBinSize(TimeRange range)
    {
        var span = range.End - range.Start;
        return span.TotalHours switch
        {
            <= 2 => "5m",
            <= 12 => "15m",
            <= 48 => "1h",
            <= 168 => "4h",
            _ => "1d"
        };
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static Azure.Monitor.Query.QueryTimeRange QueryTimeRange(TimeRange r) =>
        new(r.Start, r.End);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryTableAsync(
        string kql, Azure.Monitor.Query.QueryTimeRange timeRange, CancellationToken ct)
    {
        var response = await _client.QueryResourceAsync(_resourceId, kql, timeRange, cancellationToken: ct).ConfigureAwait(false);
        if (!response.HasValue) return [];

        var table = response.Value.Table;
        var columns = table.Columns.Select(c => c.Name).ToList();

        return table.Rows.Select(row =>
        {
            var dict = new Dictionary<string, object?>();
            for (var i = 0; i < columns.Count; i++)
                dict[columns[i]] = row[i];
            return (IReadOnlyDictionary<string, object?>)dict;
        }).ToList();
    }

    private async Task<IReadOnlyDictionary<string, object?>?> QuerySingleRowAsync(
        string kql, Azure.Monitor.Query.QueryTimeRange timeRange, CancellationToken ct)
    {
        var rows = await QueryTableAsync(kql, timeRange, ct).ConfigureAwait(false);
        return rows.Count > 0 ? rows[0] : null;
    }

    private async Task<T> QuerySingleValueAsync<T>(
        string kql, Azure.Monitor.Query.QueryTimeRange timeRange, CancellationToken ct)
    {
        var row = await QuerySingleRowAsync(kql, timeRange, ct).ConfigureAwait(false);
        if (row is null) return default!;
        var val = row.Values.FirstOrDefault();
        if (val is T typed) return typed;
        if (val is null) return default!;
        return (T)Convert.ChangeType(val, typeof(T));
    }

    private async Task<IReadOnlyList<TimeSeriesPoint>> QuerySeriesAsync(
        string kql, Azure.Monitor.Query.QueryTimeRange timeRange, CancellationToken ct)
    {
        var rows = await QueryTableAsync(kql, timeRange, ct).ConfigureAwait(false);
        return rows
            .Select(row =>
            {
                var ts = row.TryGetValue("timestamp", out var t) ? t : null;
                var v = row.TryGetValue("Value", out var val) ? val : null;
                if (ts is null || v is null) return null;
                var time = ts is DateTimeOffset dto ? dto : DateTimeOffset.Parse(ts.ToString()!);
                var value = Convert.ToDouble(v);
                return (TimeSeriesPoint?)new TimeSeriesPoint(time, value);
            })
            .OfType<TimeSeriesPoint>()
            .ToList();
    }

    private static LogRow MapLogRow(IReadOnlyDictionary<string, object?> row) => new(row);

    private static long GetLong(IReadOnlyDictionary<string, object?> row, string col) =>
        row.TryGetValue(col, out var v) && v is not null ? Convert.ToInt64(v) : 0;

    private static double GetDouble(IReadOnlyDictionary<string, object?> row, string col) =>
        row.TryGetValue(col, out var v) && v is not null ? Convert.ToDouble(v) : 0;

    private static bool GetBool(IReadOnlyDictionary<string, object?> row, string col) =>
        row.TryGetValue(col, out var v) && v is not null && Convert.ToBoolean(v);

    private static string GetString(IReadOnlyDictionary<string, object?> row, string col) =>
        row.TryGetValue(col, out var v) ? v?.ToString() ?? string.Empty : string.Empty;

    private static string? TryGetString(IReadOnlyDictionary<string, object?> row, string col) =>
        row.TryGetValue(col, out var v) ? v?.ToString() : null;

    private static DateTimeOffset GetDateTimeOffset(IReadOnlyDictionary<string, object?> row, string col)
    {
        if (!row.TryGetValue(col, out var v) || v is null) return DateTimeOffset.MinValue;
        if (v is DateTimeOffset dto) return dto;
        return DateTimeOffset.TryParse(v.ToString(), out var parsed) ? parsed : DateTimeOffset.MinValue;
    }

    private static LogQueryResult EmptyResult(TimeSpan elapsed) =>
        new([], [], elapsed, false);
}
