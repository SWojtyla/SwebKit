namespace SwebKit.Core.Domain;

public class SavedQuery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public QueryArea Area { get; set; } = QueryArea.Logs;
    public required string QueryText { get; set; }
    public string? DefaultTimeRange { get; set; } = "15m";
    public bool IsBuiltIn { get; set; }
}

public static class BuiltInQueries
{
    public static IReadOnlyList<SavedQuery> All =>
    [
        new() { Name = "Errors last 15m", Area = QueryArea.Logs, IsBuiltIn = true, DefaultTimeRange = "15m",
            QueryText = "union exceptions, traces | where timestamp > ago(15m) | where severityLevel >= 3 | order by timestamp desc" },
        new() { Name = "Slow requests (>2s)", Area = QueryArea.Logs, IsBuiltIn = true, DefaultTimeRange = "1h",
            QueryText = "requests | where timestamp > ago(1h) | where duration > 2000 | order by duration desc" },
        new() { Name = "Exceptions by type", Area = QueryArea.Logs, IsBuiltIn = true, DefaultTimeRange = "1h",
            QueryText = "exceptions | where timestamp > ago(1h) | summarize count() by type | order by count_ desc" },
        new() { Name = "Dependency failures", Area = QueryArea.Logs, IsBuiltIn = true, DefaultTimeRange = "1h",
            QueryText = "dependencies | where timestamp > ago(1h) | where success == false | order by timestamp desc" },
        new() { Name = "Find by correlation ID", Area = QueryArea.Logs, IsBuiltIn = true, DefaultTimeRange = "24h",
            QueryText = "union * | where timestamp > ago(24h) | where operation_Id == '{correlationId}'" },
    ];
}
