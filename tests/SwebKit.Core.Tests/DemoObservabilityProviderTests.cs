using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class DemoObservabilityProviderTests
{
    private readonly DemoObservabilityProvider _provider = new();
    private static readonly TimeRange DefaultRange = TimeRange.Last24Hours;

    // ── Overview ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOverviewAsync_ReturnsPositiveRequestCount()
    {
        var result = await _provider.GetOverviewAsync(DefaultRange);

        Assert.True(result.RequestCount > 0);
    }

    [Fact]
    public async Task GetOverviewAsync_FailureRateIsBetweenZeroAndOne()
    {
        var result = await _provider.GetOverviewAsync(DefaultRange);

        Assert.InRange(result.FailureRate, 0.0, 1.0);
    }

    [Fact]
    public async Task GetOverviewAsync_TrendSeriesSpanTimeRange()
    {
        var range = TimeRange.Last24Hours;
        var result = await _provider.GetOverviewAsync(range);

        Assert.NotEmpty(result.RequestTrend);
        Assert.NotEmpty(result.FailureTrend);
        Assert.All(result.RequestTrend, p => Assert.InRange(p.Timestamp, range.Start, range.End));
    }

    [Fact]
    public async Task GetOverviewAsync_AvailabilityPctIsReasonable()
    {
        var result = await _provider.GetOverviewAsync(DefaultRange);

        Assert.InRange(result.AvailabilityPct, 0.0, 100.0);
    }

    // ── Failures ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTopExceptionsAsync_ReturnsNonEmptyList()
    {
        var result = await _provider.GetTopExceptionsAsync(DefaultRange);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetTopExceptionsAsync_AllItemsHaveExceptionTypeAndCount()
    {
        var result = await _provider.GetTopExceptionsAsync(DefaultRange);

        Assert.All(result, g =>
        {
            Assert.False(string.IsNullOrWhiteSpace(g.ExceptionType));
            Assert.True(g.Count > 0);
        });
    }

    [Fact]
    public async Task GetTopExceptionsAsync_ResultsOrderedByCountDescending()
    {
        var result = await _provider.GetTopExceptionsAsync(DefaultRange);

        var counts = result.Select(g => g.Count).ToList();
        Assert.Equal(counts.OrderByDescending(c => c).ToList(), counts);
    }

    [Fact]
    public async Task GetTopExceptionsAsync_RespectsTopParameter()
    {
        var result = await _provider.GetTopExceptionsAsync(DefaultRange, top: 2);

        Assert.True(result.Count <= 2);
    }

    [Fact]
    public async Task GetExceptionSamplesAsync_ReturnsSamplesForGivenType()
    {
        var result = await _provider.GetExceptionSamplesAsync("System.NullReferenceException", DefaultRange);

        Assert.NotEmpty(result);
        Assert.All(result, row =>
        {
            Assert.True(row.Columns.ContainsKey("type"));
            Assert.Equal("System.NullReferenceException", row.Columns["type"]?.ToString());
        });
    }

    // ── Performance ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOperationPerformanceAsync_ReturnsNonEmptyList()
    {
        var result = await _provider.GetOperationPerformanceAsync(DefaultRange);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetOperationPerformanceAsync_AllItemsHaveNonEmptyOperationName()
    {
        var result = await _provider.GetOperationPerformanceAsync(DefaultRange);

        Assert.All(result, op => Assert.False(string.IsNullOrWhiteSpace(op.OperationName)));
    }

    [Fact]
    public async Task GetOperationPerformanceAsync_PercentilesAreNonNegativeAndAscending()
    {
        var result = await _provider.GetOperationPerformanceAsync(DefaultRange);

        Assert.All(result, op =>
        {
            Assert.True(op.P50Ms >= 0);
            Assert.True(op.P95Ms >= op.P50Ms);
            Assert.True(op.P99Ms >= op.P95Ms);
        });
    }

    [Fact]
    public async Task GetOperationPerformanceAsync_FailureRateIsBetweenZeroAndOne()
    {
        var result = await _provider.GetOperationPerformanceAsync(DefaultRange);

        Assert.All(result, op => Assert.InRange(op.FailureRate, 0.0, 1.0));
    }

    // ── Logs ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunQueryAsync_ReturnsResultWithColumns()
    {
        var result = await _provider.RunQueryAsync("requests | take 10", DefaultRange);

        Assert.NotEmpty(result.ColumnNames);
        Assert.NotEmpty(result.Rows);
    }

    [Fact]
    public async Task RunQueryAsync_EachRowContainsAllColumns()
    {
        var result = await _provider.RunQueryAsync("requests | take 10", DefaultRange);

        Assert.All(result.Rows, row =>
            Assert.Equal(result.ColumnNames.Count, row.Columns.Count));
    }

    [Fact]
    public async Task RunQueryAsync_RespectsMaxRowsParameter()
    {
        var result = await _provider.RunQueryAsync("traces | take 100", DefaultRange, maxRows: 5);

        Assert.True(result.Rows.Count <= 5);
    }

    [Fact]
    public async Task RunQueryAsync_TruncatedIsFalseWhenUnderLimit()
    {
        var result = await _provider.RunQueryAsync("requests | take 10", DefaultRange, maxRows: 500);

        Assert.False(result.Truncated);
    }

    // ── Availability ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAvailabilityAsync_ReturnsNonEmptyList()
    {
        var result = await _provider.GetAvailabilityAsync(DefaultRange);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetAvailabilityAsync_AllItemsHaveTestNameAndLocation()
    {
        var result = await _provider.GetAvailabilityAsync(DefaultRange);

        Assert.All(result, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.TestName));
            Assert.False(string.IsNullOrWhiteSpace(r.Location));
        });
    }

    [Fact]
    public async Task GetAvailabilityAsync_ContainsBothPassAndFailResults()
    {
        var result = await _provider.GetAvailabilityAsync(DefaultRange);

        Assert.Contains(result, r => r.Success);
        Assert.Contains(result, r => !r.Success);
    }

    // ── Dimension breakdown ──────────────────────────────────────────────────

    [Fact]
    public async Task GetDimensionBreakdownAsync_SemanticCloudRoleKey_ReturnsRoleEntries()
    {
        var result = await _provider.GetDimensionBreakdownAsync(DefaultRange, "cloud/roleName");

        Assert.NotEmpty(result.TopEntries);
        Assert.Contains(result.TopEntries, entry => entry.Value == "orders-api");
    }

    [Fact]
    public async Task GetDimensionBreakdownAsync_SemanticOperationKey_ReturnsOperationEntries()
    {
        var result = await _provider.GetDimensionBreakdownAsync(DefaultRange, "operation/name");

        Assert.NotEmpty(result.TopEntries);
        Assert.Contains(result.TopEntries, entry => entry.Value == "GET /api/orders");
    }

    // ── Presets ───────────────────────────────────────────────────────────────

    [Fact]
    public void GetPresets_ReturnsNonEmptyList()
    {
        var presets = _provider.GetPresets();

        Assert.NotEmpty(presets);
    }

    [Fact]
    public void GetPresets_AllPresetsHaveUniqueIds()
    {
        var presets = _provider.GetPresets();

        var ids = presets.Select(p => p.Id).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
    }

    [Fact]
    public void GetPresets_AllPresetsHaveNonEmptyQuery()
    {
        var presets = _provider.GetPresets();

        Assert.All(presets, p => Assert.False(string.IsNullOrWhiteSpace(p.Query)));
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOverviewAsync_ThrowsOnCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _provider.GetOverviewAsync(DefaultRange, cts.Token));
    }
}
