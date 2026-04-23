using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.AspNetCore.Components;
using SwebKit.App.Components.Observability;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public sealed class ObservabilityFailuresTabTests : TestContext
{
    public ObservabilityFailuresTabTests()
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
        Services.AddSingleton(sp => new IncidentInvestigationLauncher(sp.GetRequiredService<NavigationManager>()));
    }

    [Fact]
    public void InitialLoad_RendersSummaryAndExceptionRows()
    {
        var now = DateTimeOffset.UtcNow;
        var provider = new FakeObservabilityProvider
        {
            TopExceptions =
            [
                new ExceptionGroup(
                    "System.InvalidOperationException",
                    "problem-1",
                    122,
                    now.AddMinutes(-5),
                    "invalid state",
                    "stack line 1"),
                new ExceptionGroup(
                    "System.TimeoutException",
                    "problem-2",
                    12,
                    now.AddMinutes(-15),
                    "timed out",
                    null)
            ]
        };

        var cut = RenderComponent<ObservabilityFailures>(ps => ps
            .Add(p => p.Provider, provider)
            .Add(p => p.Range, TimeRange.Last24Hours)
            .Add(p => p.TzOffsetMinutes, 0));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Exceptions by impact", cut.Markup);
            Assert.Contains("Exception groups", cut.Markup);
            Assert.Contains("Total occurrences", cut.Markup);
            Assert.Contains("InvalidOperationException", cut.Markup);
            Assert.Contains("TimeoutException", cut.Markup);
            Assert.Equal(1, provider.TopExceptionsCallCount);
        });
    }

    [Fact]
    public async Task OnParametersSet_SameProviderAndRange_DoesNotReload()
    {
        var provider = new FakeObservabilityProvider
        {
            TopExceptions =
            [
                new ExceptionGroup(
                    "System.Exception",
                    "problem-1",
                    8,
                    DateTimeOffset.UtcNow,
                    "sample",
                    null)
            ]
        };

        var cut = RenderComponent<ObservabilityFailures>(ps => ps
            .Add(p => p.Provider, provider)
            .Add(p => p.Range, TimeRange.Last24Hours)
            .Add(p => p.TzOffsetMinutes, 0));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("System.Exception", cut.Markup);
            Assert.Equal(1, provider.TopExceptionsCallCount);
        });

        cut.SetParametersAndRender(ps => ps
            .Add(p => p.Provider, provider)
            .Add(p => p.Range, TimeRange.Last24Hours)
            .Add(p => p.TzOffsetMinutes, 120));

        cut.WaitForAssertion(() => Assert.Contains("System.Exception", cut.Markup));
        await Task.Delay(150);
        Assert.Equal(1, provider.TopExceptionsCallCount);
    }

    [Fact]
    public void ProviderError_ShowsErrorCallout()
    {
        var provider = new FakeObservabilityProvider
        {
            TopExceptionsError = new InvalidOperationException("failure query failed")
        };

        var cut = RenderComponent<ObservabilityFailures>(ps => ps
            .Add(p => p.Provider, provider)
            .Add(p => p.Range, TimeRange.Last24Hours)
            .Add(p => p.TzOffsetMinutes, 0));

        cut.WaitForAssertion(() => Assert.Contains("failure query failed", cut.Markup));
    }

    private sealed class FakeObservabilityProvider : IObservabilityProvider
    {
        public int TopExceptionsCallCount { get; private set; }

        public IReadOnlyList<ExceptionGroup> TopExceptions { get; set; } = [];
        public IReadOnlyList<LogRow> Samples { get; set; } = [];
        public Exception? TopExceptionsError { get; set; }

        public string ProviderType => "Fake";

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

        public Task<IReadOnlyList<ExceptionGroup>> GetTopExceptionsAsync(TimeRange range, int top = 20, CancellationToken ct = default)
        {
            TopExceptionsCallCount++;
            if (TopExceptionsError is not null)
            {
                throw TopExceptionsError;
            }

            return Task.FromResult(TopExceptions);
        }

        public Task<IReadOnlyList<LogRow>> GetExceptionSamplesAsync(string exceptionType, TimeRange range, CancellationToken ct = default) =>
            Task.FromResult(Samples);

        public Task<IReadOnlyList<OperationPerformance>> GetOperationPerformanceAsync(TimeRange range, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OperationPerformance>>([]);

        public Task<LogQueryResult> RunQueryAsync(string query, TimeRange range, int maxRows = 500, CancellationToken ct = default) =>
            Task.FromResult(new LogQueryResult([], [], TimeSpan.Zero, false));

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
