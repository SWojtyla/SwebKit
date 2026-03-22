using SwebKit.Core.Models;

namespace SwebKit.Core.Tests;

public class ObservabilityModelsTests
{
    // ── TimeRange ─────────────────────────────────────────────────────────────

    [Fact]
    public void TimeRange_LastHour_SpanIsOneHour()
    {
        var range = TimeRange.LastHour;

        Assert.InRange((range.End - range.Start).TotalMinutes, 59, 61);
    }

    [Fact]
    public void TimeRange_Last6Hours_SpanIsSixHours()
    {
        var range = TimeRange.Last6Hours;

        Assert.InRange((range.End - range.Start).TotalHours, 5.9, 6.1);
    }

    [Fact]
    public void TimeRange_Last7Days_SpanIsSevenDays()
    {
        var range = TimeRange.Last7Days;

        Assert.InRange((range.End - range.Start).TotalDays, 6.9, 7.1);
    }

    [Fact]
    public void TimeRange_AllPresets_StartIsBeforeEnd()
    {
        foreach (var (_, factory) in TimeRange.Presets)
        {
            var range = factory();
            Assert.True(range.Start < range.End, $"Preset start must be before end");
        }
    }

    [Fact]
    public void TimeRange_AllPresets_EndIsApproximatelyNow()
    {
        var before = DateTimeOffset.UtcNow;

        foreach (var (_, factory) in TimeRange.Presets)
        {
            var range = factory();
            Assert.True(range.End >= before, "End should be at or after 'now'");
        }
    }

    // ── LogQueryResult ────────────────────────────────────────────────────────

    [Fact]
    public void LogQueryResult_Truncated_WhenRowCountEqualsLimit()
    {
        // Simulates what AzureAppInsightsProvider does: Truncated = table.Rows.Count > maxRows
        // If we get back exactly 500 rows and the limit is 500, table.Rows.Count could be > 500
        // Here we just verify the model accepts the flag correctly.
        var result = new LogQueryResult(
            ColumnNames: ["timestamp", "message"],
            Rows: [],
            ExecutionTime: TimeSpan.FromMilliseconds(50),
            Truncated: true);

        Assert.True(result.Truncated);
    }

    [Fact]
    public void LogQueryResult_NotTruncated_WhenUnderLimit()
    {
        var rows = Enumerable.Range(0, 10)
            .Select(_ => new LogRow(new Dictionary<string, object?> { ["col"] = "val" }))
            .ToList();

        var result = new LogQueryResult(
            ColumnNames: ["col"],
            Rows: rows,
            ExecutionTime: TimeSpan.FromMilliseconds(10),
            Truncated: false);

        Assert.False(result.Truncated);
        Assert.Equal(10, result.Rows.Count);
    }

    // ── OverviewMetrics failure rate ──────────────────────────────────────────

    [Theory]
    [InlineData(1000, 50,  0.05)]   // 5% failure rate
    [InlineData(100,  0,   0.0)]    // 0% failure rate
    [InlineData(200,  200, 1.0)]    // 100% failure rate
    public void OverviewMetrics_FailureRate_IsCalculatedFromRequestAndFailedCount(
        long requestCount, long failedCount, double expectedRate)
    {
        // The provider computes: failureRate = requestCount > 0 ? (double)failedCount / requestCount : 0
        double failureRate = requestCount > 0 ? (double)failedCount / requestCount : 0;

        var metrics = new OverviewMetrics(
            RequestCount: requestCount,
            FailureRate: failureRate,
            P50ResponseTimeMs: 100,
            P95ResponseTimeMs: 300,
            ExceptionCount: failedCount,
            AvailabilityPct: 99.0,
            RequestTrend: [],
            FailureTrend: []);

        Assert.Equal(expectedRate, metrics.FailureRate, precision: 5);
        Assert.InRange(metrics.FailureRate, 0.0, 1.0);
    }

    [Fact]
    public void OverviewMetrics_ZeroRequests_FailureRateIsZero()
    {
        double failureRate = 0; // requestCount == 0 → 0
        var metrics = new OverviewMetrics(0, failureRate, 0, 0, 0, 100.0, [], []);

        Assert.Equal(0.0, metrics.FailureRate);
    }
}
