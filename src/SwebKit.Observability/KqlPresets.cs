using SwebKit.Core.Models;

namespace SwebKit.Observability;

/// <summary>
/// Built-in KQL query presets for Azure Application Insights.
/// The $timeRange placeholder is substituted at runtime with the selected time range.
/// </summary>
internal static class KqlPresets
{
    public static readonly IReadOnlyList<QueryPreset> All =
    [
        new("top-exceptions",
            "Top Exceptions",
            "Most frequent exception types by count",
            "exceptions\n| summarize Count = count() by type, problemId\n| order by Count desc\n| take 20"),

        new("failed-requests",
            "Failed Requests",
            "HTTP 4xx/5xx grouped by operation name and result code",
            "requests\n| where success == false\n| summarize Count = count() by name, resultCode\n| order by Count desc"),

        new("slow-requests",
            "Slow Requests (P95 > 1s)",
            "Operations where P95 duration exceeds 1 second",
            "requests\n| summarize P95 = percentile(duration, 95), Count = count() by name\n| where P95 > 1000\n| order by P95 desc"),

        new("dependency-failures",
            "Dependency Failures",
            "Failed external calls by target host and dependency type",
            "dependencies\n| where success == false\n| summarize Count = count() by target, name, type\n| order by Count desc"),

        new("custom-events",
            "Custom Events",
            "All custom events with their property bags",
            "customEvents\n| project timestamp, name, customDimensions\n| order by timestamp desc\n| take 100"),

        new("availability-timeline",
            "Availability Timeline",
            "Pass/fail ratio per test per hour",
            "availabilityResults\n| summarize AvailabilityPct = avg(toint(success)) * 100 by bin(timestamp, 1h), name\n| order by timestamp desc"),

        new("user-sessions",
            "Active Sessions",
            "Unique session count per hour",
            "requests\n| summarize Sessions = dcount(session_Id) by bin(timestamp, 1h)\n| order by timestamp asc"),

        new("requests-by-role",
            "Requests by Service",
            "Request volume grouped by cloud role name (microservice)",
            "requests\n| summarize Count = count() by cloud_RoleName\n| order by Count desc"),

        new("p50-p95-trend",
            "Latency Trend (P50 / P95)",
            "Hourly P50 and P95 response time trend for all requests",
            "requests\n| summarize P50 = percentile(duration, 50), P95 = percentile(duration, 95) by bin(timestamp, 1h)\n| order by timestamp asc"),

        new("top-users",
            "Top Users (by request count)",
            "Most active authenticated users",
            "requests\n| where isnotempty(user_AuthenticatedId)\n| summarize Count = count() by user_AuthenticatedId\n| order by Count desc\n| take 20"),
    ];
}
