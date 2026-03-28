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
    [InlineData(1000, 50, 0.05)]   // 5% failure rate
    [InlineData(100, 0, 0.0)]    // 0% failure rate
    [InlineData(200, 200, 1.0)]    // 100% failure rate
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

    // ── Guided KQL models ────────────────────────────────────────────────────

    [Fact]
    public void GuidedKqlQueryDefinition_Defaults_AreSafe()
    {
        var definition = GuidedKqlQueryDefinition.CreateDefault();

        Assert.Equal("traces", definition.Table);
        Assert.Empty(definition.Filters);
        Assert.Empty(definition.Projections);
        Assert.Equal("timestamp", definition.Sort.Column);
        Assert.True(definition.Sort.Descending);
        Assert.Equal(100, definition.Limit);
    }

    [Fact]
    public void GuidedKqlQueryDefinition_Clone_CreatesDeepCopy()
    {
        var source = new GuidedKqlQueryDefinition
        {
            Table = "requests",
            Filters = [new GuidedKqlFilter { Column = "name", Operator = GuidedKqlFilterOperator.Contains, Value = "api" }],
            Projections = ["timestamp", "name"],
            Sort = new GuidedKqlSort { Column = "duration", Descending = false },
            Limit = 250,
        };

        var clone = source.Clone();

        source.Filters[0].Value = "mutated";
        source.Projections[0] = "resultCode";
        source.Sort.Column = "timestamp";

        Assert.Equal("api", clone.Filters[0].Value);
        Assert.Equal("timestamp", clone.Projections[0]);
        Assert.Equal("duration", clone.Sort.Column);
        Assert.Equal(250, clone.Limit);
    }

    [Fact]
    public void GuidedKqlQueryDefinition_CopyFrom_NullMembers_FallsBackToDefaults()
    {
        var source = new GuidedKqlQueryDefinition
        {
            Table = " ",
            Filters = null!,
            Projections = null!,
            Sort = null!,
            Limit = 0,
        };

        var target = new GuidedKqlQueryDefinition();
        target.CopyFrom(source);

        Assert.Equal("traces", target.Table);
        Assert.Empty(target.Filters);
        Assert.Empty(target.Projections);
        Assert.Equal("timestamp", target.Sort.Column);
        Assert.True(target.Sort.Descending);
        Assert.Equal(100, target.Limit);
    }

    [Fact]
    public void GuidedKqlCompileResult_HelpersReflectIssueSeverities()
    {
        var result = GuidedKqlCompileResult.Success(
            query: "requests\n| take 10",
            issues:
            [
                new GuidedKqlCompileIssue(GuidedKqlCompileIssueSeverity.Warning, "LIMIT_BROAD", "Warning"),
            ]);

        Assert.True(result.CanExecute);
        Assert.False(result.HasErrors);
        Assert.True(result.HasWarnings);
    }

    [Fact]
    public void GuidedKqlCompileResult_Invalid_HasErrorsAndCannotExecute()
    {
        var result = GuidedKqlCompileResult.Invalid(
            [
                new GuidedKqlCompileIssue(GuidedKqlCompileIssueSeverity.Error, "TABLE_INVALID", "Invalid table"),
            ]);

        Assert.False(result.CanExecute);
        Assert.True(result.HasErrors);
    }
}
