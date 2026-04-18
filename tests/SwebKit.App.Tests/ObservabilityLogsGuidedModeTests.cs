using System.Reflection;
using Bunit;
using Bunit.JSInterop;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.Observability;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Observability;

namespace SwebKit.App.Tests;

public sealed class ObservabilityLogsGuidedModeTests : TestContext
{
    public ObservabilityLogsGuidedModeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
        {
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
        }

        Services.AddFluentUIComponents();
        Services.AddSingleton<INotificationService>(new NotificationService(new UiStateRepository()));
        Services.AddSingleton<IGuidedKqlCompiler>(new GuidedKqlCompiler());
    }

    [Fact]
    public void GuidedRun_CompilesAndExecutesProviderQuery()
    {
        var provider = new FakeObservabilityProvider();
        var draft = new GuidedKqlQueryDefinition
        {
            Table = "traces",
            Filters =
            [
                new GuidedKqlFilter
                {
                    Column = "message",
                    Operator = GuidedKqlFilterOperator.Contains,
                    Value = "error",
                }
            ],
            Sort = new GuidedKqlSort
            {
                Column = "timestamp",
                Descending = true,
            },
            Limit = 25,
        };

        var cut = RenderComponent<ObservabilityLogs>(ps => ps
            .Add(p => p.Provider, provider)
            .Add(p => p.Range, TimeRange.Last24Hours)
            .Add(p => p.Mode, GuidedLogsQueryMode.Guided)
            .Add(p => p.GuidedDraft, draft));

        cut.Find("[data-testid='obs-run-query']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, provider.RunQueryCallCount);
            Assert.Equal("traces\n| where message contains 'error'\n| order by timestamp desc\n| take 25", provider.LastQuery);
        });
    }

    [Fact]
    public void GuidedRun_InvalidDefinition_ShowsValidationAndDoesNotExecute()
    {
        var provider = new FakeObservabilityProvider();
        var draft = new GuidedKqlQueryDefinition
        {
            Table = "requests",
            Filters =
            [
                new GuidedKqlFilter
                {
                    Column = "duration",
                    Operator = GuidedKqlFilterOperator.GreaterThan,
                    Value = "abc",
                }
            ],
            Limit = 30,
        };

        var cut = RenderComponent<ObservabilityLogs>(ps => ps
            .Add(p => p.Provider, provider)
            .Add(p => p.Range, TimeRange.Last24Hours)
            .Add(p => p.Mode, GuidedLogsQueryMode.Guided)
            .Add(p => p.GuidedDraft, draft));

        var runButton = cut.Find("[data-testid='obs-run-query']");

        Assert.Equal("true", runButton.GetAttribute("aria-disabled"));
        Assert.NotNull(cut.Find("[data-testid='obs-guided-run-guardrail']"));
        Assert.Contains("not a valid number", cut.Markup);
        Assert.Equal(0, provider.RunQueryCallCount);
    }

    [Fact]
    public void GuidedRun_LimitWarning_RemainsRunnable()
    {
        var provider = new FakeObservabilityProvider();
        var draft = new GuidedKqlQueryDefinition
        {
            Table = "traces",
            Limit = 2500,
        };

        var cut = RenderComponent<ObservabilityLogs>(ps => ps
            .Add(p => p.Provider, provider)
            .Add(p => p.Range, TimeRange.Last24Hours)
            .Add(p => p.Mode, GuidedLogsQueryMode.Guided)
            .Add(p => p.GuidedDraft, draft));

        var runButton = cut.Find("[data-testid='obs-run-query']");

        Assert.Equal("false", runButton.GetAttribute("aria-disabled"));
        Assert.Contains("LIMIT_BROAD", cut.Markup);

        runButton.Click();

        cut.WaitForAssertion(() => Assert.Equal(1, provider.RunQueryCallCount));
    }

    [Fact]
    public void GuidedMarkup_ExposesAccessibilityMetadataForValidationAndActions()
    {
        var provider = new FakeObservabilityProvider();
        var draft = new GuidedKqlQueryDefinition
        {
            Table = "requests",
            Filters =
            [
                new GuidedKqlFilter
                {
                    Column = "duration",
                    Operator = GuidedKqlFilterOperator.GreaterThan,
                    Value = "abc",
                }
            ],
            Limit = 10,
        };

        var cut = RenderComponent<ObservabilityLogs>(ps => ps
            .Add(p => p.Provider, provider)
            .Add(p => p.Range, TimeRange.Last24Hours)
            .Add(p => p.Mode, GuidedLogsQueryMode.Guided)
            .Add(p => p.GuidedDraft, draft));

        var guidedModeButton = cut.Find("[data-testid='obs-mode-guided']");
        var advancedModeButton = cut.Find("[data-testid='obs-mode-advanced']");
        var runButton = cut.Find("[data-testid='obs-run-query']");
        var invalidFilterValue = cut.Find("[data-testid='obs-guided-filter-value-0']");

        Assert.Equal("true", guidedModeButton.GetAttribute("aria-pressed"));
        Assert.Equal("false", advancedModeButton.GetAttribute("aria-pressed"));
        Assert.Equal("Run logs query", runButton.GetAttribute("aria-label"));
        Assert.Equal("true", invalidFilterValue.GetAttribute("aria-invalid"));
    }

    [Fact]
    public void GuidedRun_KeyboardEnter_ExecutesProviderQuery()
    {
        var provider = new FakeObservabilityProvider();
        var draft = new GuidedKqlQueryDefinition
        {
            Table = "traces",
            Limit = 20,
        };

        var cut = RenderComponent<ObservabilityLogs>(ps => ps
            .Add(p => p.Provider, provider)
            .Add(p => p.Range, TimeRange.Last24Hours)
            .Add(p => p.Mode, GuidedLogsQueryMode.Guided)
            .Add(p => p.GuidedDraft, draft));

        cut.Find("[data-testid='obs-run-query']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Equal(1, provider.RunQueryCallCount));
    }

    [Fact]
    public void GuidedToAdvanced_KeyboardSwitchCarriesCompiledQueryText()
    {
        var provider = new FakeObservabilityProvider();
        var compiler = new GuidedKqlCompiler();
        var draft = new GuidedKqlQueryDefinition
        {
            Table = "traces",
            Filters =
            [
                new GuidedKqlFilter
                {
                    Column = "message",
                    Operator = GuidedKqlFilterOperator.Contains,
                    Value = "timeout",
                }
            ],
            Sort = new GuidedKqlSort
            {
                Column = "timestamp",
                Descending = true,
            },
            Limit = 10,
        };

        var expectedQuery = compiler.Compile(draft).Query;

        var cut = RenderComponent<ObservabilityLogs>(ps => ps
            .Add(p => p.Provider, provider)
            .Add(p => p.Range, TimeRange.Last24Hours)
            .Add(p => p.Mode, GuidedLogsQueryMode.Guided)
            .Add(p => p.GuidedDraft, draft));

        cut.Find("[data-testid='obs-mode-advanced']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        var queryField = typeof(ObservabilityLogs).GetField("_query", BindingFlags.NonPublic | BindingFlags.Instance);
        var carriedQuery = queryField?.GetValue(cut.Instance) as string;

        Assert.Equal(expectedQuery, carriedQuery);
    }

    private sealed class FakeObservabilityProvider : IObservabilityProvider
    {
        public string ProviderType => "Fake";

        public int RunQueryCallCount { get; private set; }

        public string LastQuery { get; private set; } = string.Empty;

        public Task<OverviewMetrics> GetOverviewAsync(TimeRange range, CancellationToken ct = default) =>
            Task.FromResult(new OverviewMetrics(
                0,
                0,
                0,
                0,
                0,
                0,
                [],
                []));

        public Task<IReadOnlyList<ExceptionGroup>> GetTopExceptionsAsync(TimeRange range, int top = 20, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExceptionGroup>>([]);

        public Task<IReadOnlyList<LogRow>> GetExceptionSamplesAsync(string exceptionType, TimeRange range, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LogRow>>([]);

        public Task<IReadOnlyList<OperationPerformance>> GetOperationPerformanceAsync(TimeRange range, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OperationPerformance>>([]);

        public Task<LogQueryResult> RunQueryAsync(string query, TimeRange range, int maxRows = 500, CancellationToken ct = default)
        {
            RunQueryCallCount++;
            LastQuery = query;

            return Task.FromResult(new LogQueryResult(
                ["timestamp", "message"],
                [new LogRow(new Dictionary<string, object?> { ["timestamp"] = DateTimeOffset.UtcNow, ["message"] = "ok" })],
                TimeSpan.FromMilliseconds(10),
                false));
        }

        public Task<IReadOnlyList<AvailabilityResult>> GetAvailabilityAsync(TimeRange range, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AvailabilityResult>>([]);

        public Task<IReadOnlyList<LatencyDataPoint>> GetOperationLatencyTrendAsync(string operationName, TimeRange range, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LatencyDataPoint>>([]);

        public IReadOnlyList<QueryPreset> GetPresets() => [];

        public Task<DependencyHealthSummary> GetDependencyHealthAsync(TimeRange range, int maxDependencies = 20, CancellationToken ct = default) =>
            Task.FromResult(new DependencyHealthSummary([], false, maxDependencies));

        public Task<DimensionBreakdown> GetDimensionBreakdownAsync(TimeRange range, string dimensionKey, int topN = 15, CancellationToken ct = default) =>
            Task.FromResult(new DimensionBreakdown(dimensionKey, [], false, topN));
    }
}