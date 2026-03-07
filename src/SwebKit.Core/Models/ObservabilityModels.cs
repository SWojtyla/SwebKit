using SwebKit.Core.Domain;

namespace SwebKit.Core.Models;

public class LogQuery
{
    public string TimeRange { get; set; } = "15m";
    public List<LogLevel> Levels { get; set; } = [];
    public string? TextSearch { get; set; }
    public string? CorrelationId { get; set; }
    public string? OperationName { get; set; }
    public List<PropertyFilter> PropertyFilters { get; set; } = [];
    public int MaxRows { get; set; } = 200;
    public string? RawKql { get; set; }
}

public class PropertyFilter
{
    public required string Key { get; set; }
    public required string Value { get; set; }
}

public class LogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public required string Message { get; set; }
    public string? OperationName { get; set; }
    public string? OperationId { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, object> Properties { get; set; } = [];
    public ObservabilityProviderType SourceProvider { get; set; }
}

public class TraceTimeline
{
    public required string RootOperationId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public List<TraceSpan> Spans { get; set; } = [];
}

public class TraceSpan
{
    public required string SpanId { get; set; }
    public string? ParentSpanId { get; set; }
    public required string Name { get; set; }
    public SpanKind Kind { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public TimeSpan Duration { get; set; }
    public SpanStatus Status { get; set; }
    public Dictionary<string, string> Tags { get; set; } = [];
    public List<SpanEvent> Events { get; set; } = [];
    public int Depth { get; set; }
}

public class SpanEvent
{
    public required string Name { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = [];
}

public class MetricsQuery
{
    public required string MetricName { get; set; }
    public string TimeRange { get; set; } = "1h";
    public TimeSpan Granularity { get; set; } = TimeSpan.FromMinutes(5);
    public Dictionary<string, string> Filters { get; set; } = [];
}

public class MetricSeries
{
    public required string MetricName { get; set; }
    public List<MetricDataPoint> DataPoints { get; set; } = [];
    public string? Unit { get; set; }
}

public class MetricDataPoint
{
    public DateTimeOffset Timestamp { get; set; }
    public double Value { get; set; }
}
