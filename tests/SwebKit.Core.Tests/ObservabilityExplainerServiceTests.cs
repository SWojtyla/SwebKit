using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class ObservabilityExplainerServiceTests
{
    private static readonly TimeRange DefaultRange = TimeRange.Last24Hours;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly ObservabilityExplainerService _sut = new();

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetExplainerSummaryAsync_HealthyDeps_HasAnomaliesFalse()
    {
        var provider = new StubProvider(
            deps: [new("payments-svc", "Http", 1000, 0.01, 50, 200)],
            pivots: new Dictionary<string, IReadOnlyList<DimensionBreakdownEntry>>
            {
                ["tenant"] = [new("t1", 100, 0.01)],
            });

        var result = await _sut.GetExplainerSummaryAsync(provider, DefaultRange, ["tenant"]);

        Assert.False(result.HasAnomalies);
    }

    [Fact]
    public async Task GetExplainerSummaryAsync_HighFailureRateDep_HasAnomaliesTrueAndSetsTopDependencyName()
    {
        var provider = new StubProvider(
            deps: [new("bad-svc", "Http", 500, 0.25, 100, 400)],
            pivots: new Dictionary<string, IReadOnlyList<DimensionBreakdownEntry>>
            {
                ["tenant"] = [new("t1", 100, 0.01)],
            });

        var result = await _sut.GetExplainerSummaryAsync(provider, DefaultRange, ["tenant"]);

        Assert.True(result.HasAnomalies);
        Assert.Equal("bad-svc", result.TopDependencyName);
    }

    [Fact]
    public async Task GetExplainerSummaryAsync_HighFailureRateDimension_HasAnomaliesTrueAndSetsTopDimensionKey()
    {
        var provider = new StubProvider(
            deps: [new("ok-svc", "Http", 1000, 0.01, 50, 200)],
            pivots: new Dictionary<string, IReadOnlyList<DimensionBreakdownEntry>>
            {
                ["tenant"] = [new("bad-tenant", 200, 0.30)],
            });

        var result = await _sut.GetExplainerSummaryAsync(provider, DefaultRange, ["tenant"]);

        Assert.True(result.HasAnomalies);
        Assert.Equal("tenant", result.TopDimensionKey);
    }

    [Fact]
    public async Task GetExplainerSummaryAsync_MultipleDimensionKeys_FetchesOneBreakdownPerKey()
    {
        var provider = new StubProvider(
            deps: [new("svc", "Http", 100, 0.0, 10, 40)],
            pivots: new Dictionary<string, IReadOnlyList<DimensionBreakdownEntry>>
            {
                ["dim1"] = [new("v1", 10, 0.0)],
                ["dim2"] = [new("v2", 20, 0.0)],
                ["dim3"] = [new("v3", 30, 0.0)],
            });

        var result = await _sut.GetExplainerSummaryAsync(provider, DefaultRange, ["dim1", "dim2", "dim3"]);

        Assert.Equal(3, result.DimensionPivots.Count);
        Assert.Contains(result.DimensionPivots, p => p.DimensionKey == "dim1");
        Assert.Contains(result.DimensionPivots, p => p.DimensionKey == "dim2");
        Assert.Contains(result.DimensionPivots, p => p.DimensionKey == "dim3");
    }

    [Fact]
    public async Task GetExplainerSummaryAsync_EmptyDepsAndPivots_HasAnomaliesFalseAndNullTopNames()
    {
        var provider = new StubProvider(
            deps: [],
            pivots: new Dictionary<string, IReadOnlyList<DimensionBreakdownEntry>>
            {
                ["tenant"] = [],
            });

        var result = await _sut.GetExplainerSummaryAsync(provider, DefaultRange, ["tenant"]);

        Assert.False(result.HasAnomalies);
        Assert.Null(result.TopDependencyName);
        Assert.Null(result.TopDimensionKey);
    }

    // ── Wave 2: Deployment comparison ─────────────────────────────────────────

    [Fact]
    public async Task GetDeploymentComparisonAsync_NoChange_HasRegressionFalse()
    {
        var anchorTime = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var anchor = new DeploymentAnchor(Guid.NewGuid(), "v1.0", anchorTime);
        var window = TimeSpan.FromHours(1);
        var metrics = MakeOverview(failureRate: 0.05, p95: 200);
        var provider = new StubProviderWithOverride(_ => metrics);

        var result = await _sut.GetDeploymentComparisonAsync(provider, anchor, window);

        Assert.False(result.HasRegression);
    }

    [Fact]
    public async Task GetDeploymentComparisonAsync_FailureRateRisesOver10Points_HasRegressionTrue()
    {
        var anchorTime = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var anchor = new DeploymentAnchor(Guid.NewGuid(), "v1.1", anchorTime);
        var window = TimeSpan.FromHours(1);
        var beforeWindow = new TimeRange(anchorTime - window, anchorTime);
        var provider = new StubProviderWithOverride(range =>
            range == beforeWindow
                ? MakeOverview(failureRate: 0.05, p95: 200)
                : MakeOverview(failureRate: 0.16, p95: 200));

        var result = await _sut.GetDeploymentComparisonAsync(provider, anchor, window);

        Assert.True(result.HasRegression);
    }

    [Fact]
    public async Task GetDeploymentComparisonAsync_P95RisesOver20Pct_HasRegressionTrue()
    {
        var anchorTime = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var anchor = new DeploymentAnchor(Guid.NewGuid(), "v1.2", anchorTime);
        var window = TimeSpan.FromHours(1);
        var beforeWindow = new TimeRange(anchorTime - window, anchorTime);
        var provider = new StubProviderWithOverride(range =>
            range == beforeWindow
                ? MakeOverview(failureRate: 0.05, p95: 200)
                : MakeOverview(failureRate: 0.05, p95: 241));

        var result = await _sut.GetDeploymentComparisonAsync(provider, anchor, window);

        Assert.True(result.HasRegression);
    }

    [Fact]
    public async Task GetDeploymentComparisonAsync_WindowsAnchoredAtDeploymentTime()
    {
        var anchorTime = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var anchor = new DeploymentAnchor(Guid.NewGuid(), "v1.3", anchorTime);
        var window = TimeSpan.FromHours(2);
        var provider = new StubProviderWithOverride(_ => MakeOverview());

        var result = await _sut.GetDeploymentComparisonAsync(provider, anchor, window);

        Assert.Equal(anchorTime, result.BeforeWindow.End);
        Assert.Equal(anchorTime, result.AfterWindow.Start);
        Assert.Equal(anchorTime - window, result.BeforeWindow.Start);
        Assert.Equal(anchorTime + window, result.AfterWindow.End);
    }

    // ── Wave 3: SLO tracking ──────────────────────────────────────────────────

    [Fact]
    public async Task GetSloStatusAsync_AllSlosMet_NoBreach()
    {
        var provider = new StubProviderWithOverride(_ => MakeOverview(failureRate: 0.01, p95: 200, availability: 99.9));
        var definitions = new List<SloDefinition>
        {
            new() { Name = "Failure Rate SLO", Metric = SloMetric.FailureRate, Target = 0.05 },
            new() { Name = "P95 SLO", Metric = SloMetric.P95ResponseTimeMs, Target = 1000 },
        };

        var result = await _sut.GetSloStatusAsync(provider, definitions, DefaultRange);

        Assert.False(result.AnyBreached);
        Assert.False(result.AnyAtRisk);
        Assert.All(result.Entries, e => Assert.Equal(SloState.Met, e.State));
    }

    [Fact]
    public async Task GetSloStatusAsync_FailureRateExceedsTarget_BreachedAndAnyBreachedTrue()
    {
        var provider = new StubProviderWithOverride(_ => MakeOverview(failureRate: 0.15));
        var definitions = new List<SloDefinition>
        {
            new() { Name = "Failure Rate SLO", Metric = SloMetric.FailureRate, Target = 0.10 },
        };

        var result = await _sut.GetSloStatusAsync(provider, definitions, DefaultRange);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(SloState.Breached, entry.State);
        Assert.True(result.AnyBreached);
        Assert.False(result.AnyAtRisk);
    }

    [Fact]
    public async Task GetSloStatusAsync_FailureRateWithinWarnBand_AtRiskAndAnyAtRiskTrue()
    {
        // Target = 0.10, WarnAt = null → effective warnAt = 0.09; current = 0.095 → AtRisk
        var provider = new StubProviderWithOverride(_ => MakeOverview(failureRate: 0.095));
        var definitions = new List<SloDefinition>
        {
            new() { Name = "Failure Rate SLO", Metric = SloMetric.FailureRate, Target = 0.10 },
        };

        var result = await _sut.GetSloStatusAsync(provider, definitions, DefaultRange);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(SloState.AtRisk, entry.State);
        Assert.True(result.AnyAtRisk);
        Assert.False(result.AnyBreached);
    }

    [Fact]
    public async Task GetSloStatusAsync_EmptyDefinitions_EmptySummaryNoBreachOrRisk()
    {
        var provider = new StubProviderWithOverride(_ => MakeOverview());

        var result = await _sut.GetSloStatusAsync(provider, [], DefaultRange);

        Assert.Empty(result.Entries);
        Assert.False(result.AnyBreached);
        Assert.False(result.AnyAtRisk);
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubProvider(
        IReadOnlyList<DependencyHealthEntry> deps,
        Dictionary<string, IReadOnlyList<DimensionBreakdownEntry>> pivots) : IObservabilityProvider
    {
        public string ProviderType => "Stub";

        public Task<DependencyHealthSummary> GetDependencyHealthAsync(
            TimeRange range, int maxDependencies = 20, CancellationToken ct = default) =>
            Task.FromResult(new DependencyHealthSummary(deps, false, maxDependencies));

        public Task<DimensionBreakdown> GetDimensionBreakdownAsync(
            TimeRange range, string dimensionKey, int topN = 15, CancellationToken ct = default) =>
            Task.FromResult(new DimensionBreakdown(
                dimensionKey,
                pivots.TryGetValue(dimensionKey, out var entries) ? entries : [],
                false,
                topN));

        public Task<OverviewMetrics> GetOverviewAsync(TimeRange range, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ExceptionGroup>> GetTopExceptionsAsync(TimeRange range, int top = 20, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<LogRow>> GetExceptionSamplesAsync(string exceptionType, TimeRange range, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<OperationPerformance>> GetOperationPerformanceAsync(TimeRange range, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<LogQueryResult> RunQueryAsync(string query, TimeRange range, int maxRows = 500, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<AvailabilityResult>> GetAvailabilityAsync(TimeRange range, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<LatencyDataPoint>> GetOperationLatencyTrendAsync(string operationName, TimeRange range, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public IReadOnlyList<QueryPreset> GetPresets() => [];
    }

    private sealed class StubProviderWithOverride(Func<TimeRange, OverviewMetrics> overviewFactory) : IObservabilityProvider
    {
        public string ProviderType => "StubOverride";

        public Task<OverviewMetrics> GetOverviewAsync(TimeRange range, CancellationToken ct = default) =>
            Task.FromResult(overviewFactory(range));

        public Task<DependencyHealthSummary> GetDependencyHealthAsync(TimeRange range, int maxDependencies = 20, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<DimensionBreakdown> GetDimensionBreakdownAsync(TimeRange range, string dimensionKey, int topN = 15, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ExceptionGroup>> GetTopExceptionsAsync(TimeRange range, int top = 20, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<LogRow>> GetExceptionSamplesAsync(string exceptionType, TimeRange range, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<OperationPerformance>> GetOperationPerformanceAsync(TimeRange range, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<LogQueryResult> RunQueryAsync(string query, TimeRange range, int maxRows = 500, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<AvailabilityResult>> GetAvailabilityAsync(TimeRange range, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<LatencyDataPoint>> GetOperationLatencyTrendAsync(string operationName, TimeRange range, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public IReadOnlyList<QueryPreset> GetPresets() => [];
    }

    private static OverviewMetrics MakeOverview(
        double failureRate = 0.01,
        double p95 = 200,
        double availability = 99.9,
        double p50 = 100) =>
        new(
            RequestCount: 1000,
            FailureRate: failureRate,
            P50ResponseTimeMs: p50,
            P95ResponseTimeMs: p95,
            ExceptionCount: 10,
            AvailabilityPct: availability,
            RequestTrend: [],
            FailureTrend: []);
}
