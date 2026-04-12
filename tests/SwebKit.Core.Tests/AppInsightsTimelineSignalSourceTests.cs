using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Observability.IncidentTimeline;

namespace SwebKit.Core.Tests;

public sealed class AppInsightsTimelineSignalSourceTests
{
    [Fact]
    public void IncidentTimelineConfig_FindWorkloadMapping_IsCaseInsensitive()
    {
        var config = new IncidentTimelineConfig
        {
            WorkloadMappings =
            [
                new IncidentTimelineWorkloadMapping
                {
                    Namespace = "PRD-PHONOTIF",
                    WorkloadKind = IncidentWorkloadKind.Deployment,
                    WorkloadName = "PHONOTIF-API",
                },
            ],
        };

        var mapping = config.FindWorkloadMapping(new IncidentWorkloadScope("ctx", "prd-phonotif", IncidentWorkloadKind.Deployment, "phonotif-api"));

        Assert.NotNull(mapping);
    }

    [Fact]
    public async Task FetchAsync_ReturnsMappedObservabilityEvidence()
    {
        var provider = new FakeObservabilityProvider(new LogQueryResult(
            ["timestamp", "RecordType", "RecordId", "Title", "Summary", "Role", "Operation", "CorrelationId", "SeverityLevel"],
            [
                new LogRow(new Dictionary<string, object?>
                {
                    ["timestamp"] = new DateTimeOffset(2026, 04, 12, 12, 30, 00, TimeSpan.Zero),
                    ["RecordType"] = "exception",
                    ["RecordId"] = "problem-1",
                    ["Title"] = "System.TimeoutException",
                    ["Summary"] = "Timed out while calling downstream service.",
                    ["Role"] = "phonotif-api",
                    ["Operation"] = "POST /notifications",
                    ["CorrelationId"] = "corr-1",
                    ["SeverityLevel"] = "3",
                }),
            ],
            TimeSpan.FromMilliseconds(10),
            false));
        var source = new AppInsightsTimelineSignalSource(
            CreateAppState(config =>
            {
                config.ObservabilityConfig = new ObservabilityConfig { SelectedResourceId = "/subscriptions/demo/resourceGroups/rg/providers/microsoft.insights/components/phonotif-ai" };
                config.IncidentTimeline.WorkloadMappings.Add(new IncidentTimelineWorkloadMapping
                {
                    Namespace = "prd-phonotif",
                    WorkloadKind = IncidentWorkloadKind.Deployment,
                    WorkloadName = "phonotif-api",
                    Observability = new IncidentTimelineObservabilityMapping
                    {
                        ResourceId = "/subscriptions/demo/resourceGroups/rg/providers/microsoft.insights/components/phonotif-ai",
                        CloudRoleNames = ["phonotif-api"],
                    },
                });
            }),
            new FakeObservabilityProviderFactory(provider));

        var result = await source.FetchAsync(CreateQuery());

        Assert.Equal(IncidentTimelineSourceCoverageState.Loaded, result.CoverageState);
        Assert.Single(result.Items);
        Assert.Equal(IncidentLinkRelevance.Corroborating, result.Items[0].PrimaryRelevance);
        Assert.Contains("explicitly mapped", result.Items[0].LinkReasons[0].Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchAsync_ReturnsUnmappedWhenNoBindingExists()
    {
        var source = new AppInsightsTimelineSignalSource(
            CreateAppState(_ => { }),
            new FakeObservabilityProviderFactory(new FakeObservabilityProvider(new LogQueryResult([], [], TimeSpan.Zero, false))));

        var result = await source.FetchAsync(CreateQuery());

        Assert.Equal(IncidentTimelineSourceCoverageState.Unmapped, result.CoverageState);
    }

    private static IncidentTimelineQuery CreateQuery() => new()
    {
        Scope = new IncidentWorkloadScope("ctx", "prd-phonotif", IncidentWorkloadKind.Deployment, "phonotif-api"),
        Window = new TimeRange(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow),
        SelectedSources = [IncidentTimelineSource.Observability],
        MaxItems = 20,
        MaxItemsPerSource = 20,
    };

    private static AppStateService CreateAppState(Action<AppConfig> configure)
    {
        var config = new AppConfig { Name = "Test" };
        configure(config);

        var repository = new ProfileRepository();
        repository.ReplaceProfileData(new ProfileData
        {
            Config = config,
        });

        return new AppStateService(repository, new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance));
    }

    private sealed class FakeObservabilityProviderFactory : IObservabilityProviderFactory
    {
        private readonly IObservabilityProvider _provider;

        public FakeObservabilityProviderFactory(IObservabilityProvider provider)
        {
            _provider = provider;
        }

        public IObservabilityProvider Create(string resourceId, bool useDemoData) => _provider;
    }

    private sealed class FakeObservabilityProvider : IObservabilityProvider
    {
        private readonly LogQueryResult _result;

        public FakeObservabilityProvider(LogQueryResult result)
        {
            _result = result;
        }

        public string ProviderType => "Fake";

        public Task<OverviewMetrics> GetOverviewAsync(TimeRange range, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExceptionGroup>> GetTopExceptionsAsync(TimeRange range, int top = 20, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<LogRow>> GetExceptionSamplesAsync(string exceptionType, TimeRange range, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<OperationPerformance>> GetOperationPerformanceAsync(TimeRange range, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<LogQueryResult> RunQueryAsync(string query, TimeRange range, int maxRows = 500, CancellationToken ct = default) => Task.FromResult(_result);

        public Task<IReadOnlyList<AvailabilityResult>> GetAvailabilityAsync(TimeRange range, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<LatencyDataPoint>> GetOperationLatencyTrendAsync(string operationName, TimeRange range, CancellationToken ct = default) => throw new NotSupportedException();

        public IReadOnlyList<QueryPreset> GetPresets() => [];
    }
}