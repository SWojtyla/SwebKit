using System.Reflection;
using System.Runtime.CompilerServices;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;
using SwebKit.WinUI.ViewModels.Observability;

namespace SwebKit.WinUI.Tests;

[Collection("AppDataSandbox")]
public sealed class ObservabilityPageViewModelTests
{
    [Fact]
    public async Task ActivateSelectedResource_LoadsOverviewAnalysisSloAndAnchors()
    {
        using var _ = new AppDataSandbox();
        var harness = CreateHarness();

        await harness.ViewModel.LoadAsync();
        SeedReleaseRepository(harness.ReleaseRepository);

        await harness.ViewModel.ActivateSelectedResourceCommand.ExecuteAsync(null);

        Assert.Equal(["cloud/roleName", "operation/name"], harness.Explainer.LastDimensionKeys);
        Assert.True(harness.ViewModel.HasDimensionBreakdowns);
        Assert.True(harness.ViewModel.HasSloStatusEntries);
        Assert.Equal("SLO at risk", harness.ViewModel.SloStatusBadgeText);

        var anchor = Assert.Single(harness.ViewModel.DeploymentAnchors);
        Assert.Equal("Release 2026.04.25", anchor.ReleaseName);
        Assert.Contains("Loaded 2", harness.ViewModel.BreakdownHeadline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectingDeploymentAnchor_LoadsComparisonSummary()
    {
        using var _ = new AppDataSandbox();
        var harness = CreateHarness();

        await harness.ViewModel.LoadAsync();
        SeedReleaseRepository(harness.ReleaseRepository);
        await harness.ViewModel.ActivateSelectedResourceCommand.ExecuteAsync(null);

        var anchor = Assert.Single(harness.ViewModel.DeploymentAnchors);
        harness.ViewModel.SelectedDeploymentAnchor = anchor;

        await WaitForAsync(() => harness.ViewModel.HasDeploymentComparison && !harness.ViewModel.IsLoadingDeploymentComparison);

        Assert.Equal(1, harness.Explainer.DeploymentComparisonCallCount);
        Assert.Equal("Regression detected", harness.ViewModel.DeploymentComparisonBadgeText);
        Assert.Equal(4, harness.ViewModel.DeploymentComparisonDeltas.Count);
        Assert.Contains("Before", harness.ViewModel.DeploymentComparisonWindowText, StringComparison.Ordinal);
        Assert.Contains("After", harness.ViewModel.DeploymentComparisonWindowText, StringComparison.Ordinal);

        var availabilityDelta = Assert.Single(harness.ViewModel.DeploymentComparisonDeltas, delta => delta.MetricLabel == "Availability");
        Assert.True(
            double.TryParse(availabilityDelta.BeforeText.TrimEnd('%'), NumberStyles.Float, CultureInfo.CurrentCulture, out var beforeAvailability));
        Assert.True(
            double.TryParse(availabilityDelta.AfterText.TrimEnd('%'), NumberStyles.Float, CultureInfo.CurrentCulture, out var afterAvailability));
        Assert.InRange(beforeAvailability, 99.9, 100.1);
        Assert.InRange(afterAvailability, 99.6, 99.8);
    }

    private static ObservabilityPageHarness CreateHarness()
    {
        var profileRepository = new ProfileRepository();
        var uiStateRepository = new UiStateRepository();
        var appState = new AppStateService(
            profileRepository,
            uiStateRepository,
            new AppEventBus(NullLogger<AppEventBus>.Instance));

        MarkInitialized(appState);
        appState.Config.ObservabilityConfig = new ObservabilityConfig
        {
            MaxRowsPerQuery = 200,
            SloDefinitions =
            [
                new SloDefinition
                {
                    Name = "P95 API",
                    Metric = SloMetric.P95ResponseTimeMs,
                    Target = 250,
                    WarnAt = 200,
                },
            ],
        };

        var navigation = new TestShellNavigationService();
        var workspaceService = new OperatorWorkspaceService(
            appState,
            uiStateRepository,
            navigation,
            Array.Empty<IOperatorResourceSearchProvider>());

        var provider = new TestObservabilityProvider();
        var discovery = new TestObservabilityDiscovery();
        var explainer = new TestObservabilityExplainerService();
        var notifications = new TestNotificationService();
        var releaseRepository = new ReleaseRepository();

        var viewModel = new ObservabilityPageViewModel(
            appState,
            discovery,
            new TestObservabilityProviderFactory(provider),
            new TestGuidedKqlCompiler(),
            explainer,
            navigation,
            notifications,
            releaseRepository,
            workspaceService,
            NullLogger<ObservabilityPageViewModel>.Instance);

        return new ObservabilityPageHarness(viewModel, releaseRepository, explainer);
    }

    private static void SeedReleaseRepository(ReleaseRepository releaseRepository)
    {
        var release = new ReleaseRecord
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Release 2026.04.25",
        };

        var snapshot = new DeploymentSnapshot
        {
            ReleaseId = release.Id,
            ComponentName = "api",
            Environment = "prod",
            DeployedAt = DateTimeOffset.Parse("2026-04-25T12:00:00Z"),
        };

        SetPrivateField(releaseRepository, "_releases", new List<ReleaseRecord> { release });
        SetPrivateField(releaseRepository, "_snapshots", new List<DeploymentSnapshot> { snapshot });
    }

    private static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }

    private static async Task WaitForAsync(Func<bool> predicate, int timeoutMilliseconds = 1000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(predicate(), "Timed out waiting for the expected Observability state.");
    }

    private static void MarkInitialized(AppStateService appState)
    {
        var initializedField = typeof(AppStateService).GetField("<IsInitialized>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        initializedField?.SetValue(appState, true);

        var initializedTcsField = typeof(AppStateService).GetField("_initializedTcs", BindingFlags.Instance | BindingFlags.NonPublic);
        var initializedTcs = (TaskCompletionSource?)initializedTcsField?.GetValue(appState);
        initializedTcs?.TrySetResult();
    }

    private sealed record ObservabilityPageHarness(
        ObservabilityPageViewModel ViewModel,
        ReleaseRepository ReleaseRepository,
        TestObservabilityExplainerService Explainer);

    private sealed class TestObservabilityDiscovery : IObservabilityResourceDiscovery
    {
        public async IAsyncEnumerable<ObservabilityResourceInfo> DiscoverResourcesAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            if (!ct.IsCancellationRequested)
            {
                yield return new ObservabilityResourceInfo(
                    "/subscriptions/sub/resourceGroups/rg/providers/microsoft.insights/components/prod-ai",
                    "prod-ai",
                    "sub",
                    "Subscription",
                    "rg",
                    "westeurope");
            }
        }
    }

    private sealed class TestObservabilityProviderFactory(TestObservabilityProvider provider) : IObservabilityProviderFactory
    {
        public IObservabilityProvider Create(string resourceId, bool useDemoData) => provider;
    }

    private sealed class TestGuidedKqlCompiler : IGuidedKqlCompiler
    {
        public GuidedKqlCompileResult Compile(GuidedKqlQueryDefinition definition) => GuidedKqlCompileResult.Success("requests | take 10");
    }

    private sealed class TestObservabilityProvider : IObservabilityProvider
    {
        public string ProviderType => "Test Observability";

        public Task<OverviewMetrics> GetOverviewAsync(TimeRange range, CancellationToken ct = default) => Task.FromResult(
            new OverviewMetrics(
                420,
                0.04,
                110,
                210,
                7,
                99.7,
                new[]
                {
                    new TimeSeriesPoint(DateTimeOffset.Parse("2026-04-25T10:00:00Z"), 150),
                    new TimeSeriesPoint(DateTimeOffset.Parse("2026-04-25T11:00:00Z"), 270),
                },
                new[]
                {
                    new TimeSeriesPoint(DateTimeOffset.Parse("2026-04-25T10:00:00Z"), 0.02),
                    new TimeSeriesPoint(DateTimeOffset.Parse("2026-04-25T11:00:00Z"), 0.04),
                }));

        public Task<IReadOnlyList<ExceptionGroup>> GetTopExceptionsAsync(TimeRange range, int top = 20, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ExceptionGroup>>([]);

        public Task<IReadOnlyList<LogRow>> GetExceptionSamplesAsync(string exceptionType, TimeRange range, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LogRow>>([]);

        public Task<IReadOnlyList<OperationPerformance>> GetOperationPerformanceAsync(TimeRange range, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OperationPerformance>>([]);

        public Task<LogQueryResult> RunQueryAsync(string query, TimeRange range, int maxRows = 500, CancellationToken ct = default)
            => Task.FromResult(new LogQueryResult([], [], TimeSpan.FromMilliseconds(25), false));

        public Task<IReadOnlyList<AvailabilityResult>> GetAvailabilityAsync(TimeRange range, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AvailabilityResult>>([]);

        public Task<IReadOnlyList<LatencyDataPoint>> GetOperationLatencyTrendAsync(string operationName, TimeRange range, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LatencyDataPoint>>([]);

        public IReadOnlyList<QueryPreset> GetPresets() =>
        [
            new QueryPreset("requests", "Requests", "Recent requests", "requests | take 20"),
        ];

        public Task<DependencyHealthSummary> GetDependencyHealthAsync(TimeRange range, int maxDependencies = 20, CancellationToken ct = default)
            => Task.FromResult(new DependencyHealthSummary([], false, maxDependencies));

        public Task<DimensionBreakdown> GetDimensionBreakdownAsync(TimeRange range, string dimensionKey, int topN = 15, CancellationToken ct = default)
            => Task.FromResult(new DimensionBreakdown(dimensionKey, [], false, topN));
    }

    private sealed class TestObservabilityExplainerService : IObservabilityExplainerService
    {
        public IReadOnlyList<string> LastDimensionKeys { get; private set; } = [];

        public int DeploymentComparisonCallCount { get; private set; }

        public Task<ObservabilityExplainerSummary> GetExplainerSummaryAsync(
            IObservabilityProvider provider,
            TimeRange range,
            IReadOnlyList<string> dimensionKeys,
            CancellationToken ct = default)
        {
            LastDimensionKeys = dimensionKeys.ToArray();

            return Task.FromResult(new ObservabilityExplainerSummary(
                new DependencyHealthSummary(
                    [
                        new DependencyHealthEntry("orders-sql", "sql", 120, 0.18, 85, 340),
                    ],
                    false,
                    20),
                [
                    new DimensionBreakdown(
                        "cloud/roleName",
                        [new DimensionBreakdownEntry("orders-api", 80, 0.15)],
                        false,
                        15),
                    new DimensionBreakdown(
                        "operation/name",
                        [new DimensionBreakdownEntry("GET /orders", 55, 0.11)],
                        false,
                        15),
                ],
                "orders-sql",
                "cloud/roleName",
                true));
        }

        public Task<DeploymentComparisonSummary> GetDeploymentComparisonAsync(
            IObservabilityProvider provider,
            DeploymentAnchor anchor,
            TimeSpan windowDuration,
            CancellationToken ct = default)
        {
            DeploymentComparisonCallCount++;

            return Task.FromResult(new DeploymentComparisonSummary(
                anchor,
                new TimeRange(anchor.AnchorTime - windowDuration, anchor.AnchorTime),
                new TimeRange(anchor.AnchorTime, anchor.AnchorTime + windowDuration),
                [
                    new MetricDelta("FailureRate", 0.01, 0.04, 300),
                    new MetricDelta("P50ResponseTimeMs", 80, 110, 37.5),
                    new MetricDelta("P95ResponseTimeMs", 160, 210, 31.25),
                    new MetricDelta("AvailabilityPct", 99.95, 99.70, -0.25),
                ],
                true));
        }

        public Task<SloStatusSummary> GetSloStatusAsync(
            IObservabilityProvider provider,
            IReadOnlyList<SloDefinition> definitions,
            TimeRange range,
            CancellationToken ct = default)
        {
            return Task.FromResult(new SloStatusSummary(
                [
                    new SloStatusEntry(definitions[0], 210, SloState.AtRisk),
                ],
                false,
                true));
        }
    }

    private sealed class TestShellNavigationService : IShellNavigationService
    {
        public string? CurrentArea { get; private set; }

        public event Action? NavigationChanged;

        public void NavigateTo(string area, object? parameter = null)
        {
            CurrentArea = area;
            NavigationChanged?.Invoke();
        }
    }

    private sealed class TestNotificationService : INotificationService
    {
        public IReadOnlyList<Notification> All => [];

        public event Action? NotificationsChanged
        {
            add { }
            remove { }
        }

        public void ShowSuccess(string message, string? detail = null)
        {
        }

        public void ShowWarning(string message, string? detail = null)
        {
        }

        public void ShowError(string message, string? detail = null, Exception? ex = null)
        {
        }

        public void ShowInfo(string message, string? detail = null)
        {
        }

        public void Dismiss(Guid id)
        {
        }

        public void ClearAll()
        {
        }
    }
}