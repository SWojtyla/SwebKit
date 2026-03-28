namespace SwebKit.Core.Models;

public record TimeRange(DateTimeOffset Start, DateTimeOffset End)
{
    public static TimeRange LastHour => new(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);
    public static TimeRange Last6Hours => new(DateTimeOffset.UtcNow.AddHours(-6), DateTimeOffset.UtcNow);
    public static TimeRange Last24Hours => new(DateTimeOffset.UtcNow.AddHours(-24), DateTimeOffset.UtcNow);
    public static TimeRange Last7Days => new(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow);
    public static TimeRange Last30Days => new(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow);

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
    string? SampleStackTrace,
    string? SampleOperationId = null);

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
    string Location,
    string? WorkspaceType = null);

public record LatencyDataPoint(DateTimeOffset Timestamp, double P50Ms, double P95Ms, double P99Ms);

public record QueryPreset(
    string Id,
    string Name,
    string Description,
    string Query);

public enum GuidedLogsQueryMode
{
    Advanced = 0,
    Guided = 1,
}

public enum GuidedKqlFilterOperator
{
    Equals = 0,
    NotEquals = 1,
    Contains = 2,
    StartsWith = 3,
    EndsWith = 4,
    GreaterThan = 5,
    GreaterThanOrEqual = 6,
    LessThan = 7,
    LessThanOrEqual = 8,
}

public sealed class GuidedKqlFilter
{
    public string Column { get; set; } = string.Empty;
    public GuidedKqlFilterOperator Operator { get; set; } = GuidedKqlFilterOperator.Equals;
    public string Value { get; set; } = string.Empty;

    public GuidedKqlFilter Clone() => new()
    {
        Column = Column,
        Operator = Operator,
        Value = Value,
    };
}

public sealed class GuidedKqlSort
{
    public string Column { get; set; } = "timestamp";
    public bool Descending { get; set; } = true;

    public GuidedKqlSort Clone() => new()
    {
        Column = Column,
        Descending = Descending,
    };
}

public sealed class GuidedKqlQueryDefinition
{
    public string Table { get; set; } = "traces";
    public List<GuidedKqlFilter> Filters { get; set; } = [];
    public List<string> Projections { get; set; } = [];
    public GuidedKqlSort Sort { get; set; } = new();
    public int Limit { get; set; } = 100;

    public static GuidedKqlQueryDefinition CreateDefault() => new();

    public GuidedKqlQueryDefinition Clone()
    {
        var clone = new GuidedKqlQueryDefinition();
        clone.CopyFrom(this);
        return clone;
    }

    public void CopyFrom(GuidedKqlQueryDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Table = string.IsNullOrWhiteSpace(source.Table) ? "traces" : source.Table;
        Filters = source.Filters?.Select(static filter => filter.Clone()).ToList() ?? [];
        Projections = source.Projections?.Select(static column => column).ToList() ?? [];
        Sort = source.Sort?.Clone() ?? new GuidedKqlSort();
        Limit = source.Limit > 0 ? source.Limit : 100;
    }
}

public enum GuidedKqlCompileIssueSeverity
{
    Error = 0,
    Warning = 1,
}

public sealed record GuidedKqlCompileIssue(
    GuidedKqlCompileIssueSeverity Severity,
    string Code,
    string Message,
    string? Field = null)
{
    public bool IsError => Severity == GuidedKqlCompileIssueSeverity.Error;
    public bool IsWarning => Severity == GuidedKqlCompileIssueSeverity.Warning;
}

public sealed class GuidedKqlCompileResult
{
    public string Query { get; init; } = string.Empty;
    public IReadOnlyList<GuidedKqlCompileIssue> Issues { get; init; } = [];

    public bool HasErrors => Issues.Any(static issue => issue.IsError);
    public bool HasWarnings => Issues.Any(static issue => issue.IsWarning);
    public bool CanExecute => !HasErrors && !string.IsNullOrWhiteSpace(Query);

    public static GuidedKqlCompileResult Success(string query, IReadOnlyList<GuidedKqlCompileIssue>? issues = null) => new()
    {
        Query = query,
        Issues = issues ?? [],
    };

    public static GuidedKqlCompileResult Invalid(IReadOnlyList<GuidedKqlCompileIssue> issues) => new()
    {
        Query = string.Empty,
        Issues = issues,
    };
}
