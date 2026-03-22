namespace SwebKit.Core.Models;

public record TimeRange(DateTimeOffset Start, DateTimeOffset End)
{
    public static TimeRange LastHour    => new(DateTimeOffset.UtcNow.AddHours(-1),  DateTimeOffset.UtcNow);
    public static TimeRange Last6Hours  => new(DateTimeOffset.UtcNow.AddHours(-6),  DateTimeOffset.UtcNow);
    public static TimeRange Last24Hours => new(DateTimeOffset.UtcNow.AddHours(-24), DateTimeOffset.UtcNow);
    public static TimeRange Last7Days   => new(DateTimeOffset.UtcNow.AddDays(-7),   DateTimeOffset.UtcNow);
    public static TimeRange Last30Days  => new(DateTimeOffset.UtcNow.AddDays(-30),  DateTimeOffset.UtcNow);

    public static readonly (string Label, Func<TimeRange> Factory)[] Presets =
    [
        ("Last 1 hour",   () => LastHour),
        ("Last 6 hours",  () => Last6Hours),
        ("Last 24 hours", () => Last24Hours),
        ("Last 7 days",   () => Last7Days),
        ("Last 30 days",  () => Last30Days),
    ];
}

public record TimeSeriesPoint(DateTimeOffset Timestamp, double Value);

public record OverviewMetrics(
    long RequestCount,
    double FailureRate,
    double P50ResponseTimeMs,
    double P95ResponseTimeMs,
    long ExceptionCount,
    double AvailabilityPct,
    IReadOnlyList<TimeSeriesPoint> RequestTrend,
    IReadOnlyList<TimeSeriesPoint> FailureTrend);

public record ExceptionGroup(
    string ExceptionType,
    string ProblemId,
    long Count,
    DateTimeOffset LastSeen,
    string? SampleMessage,
    string? SampleStackTrace);

public record LogRow(IReadOnlyDictionary<string, object?> Columns);

public record LogQueryResult(
    IReadOnlyList<string> ColumnNames,
    IReadOnlyList<LogRow> Rows,
    TimeSpan ExecutionTime,
    bool Truncated);

public record OperationPerformance(
    string OperationName,
    long RequestCount,
    double FailureRate,
    double P50Ms,
    double P95Ms,
    double P99Ms);

public record AvailabilityResult(
    string TestName,
    string Location,
    bool Success,
    DateTimeOffset Timestamp,
    double DurationMs,
    string? FailureMessage);

public record ObservabilityResourceInfo(
    string ResourceId,
    string Name,
    string SubscriptionId,
    string SubscriptionName,
    string ResourceGroup,
    string Location);

public record QueryPreset(
    string Id,
    string Name,
    string Description,
    string Query);
