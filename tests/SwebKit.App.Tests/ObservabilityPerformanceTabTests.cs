using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.Observability;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public sealed class ObservabilityPerformanceTabTests : TestContext
{
    public ObservabilityPerformanceTabTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
        {
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
        }

        Services.AddFluentUIComponents();
    }

    [Fact]
    public void InitialLoad_RendersKpisAndSeverityBadges()
    {
        var provider = new FakeObservabilityProvider
        {
            Operations =
            [
                new OperationPerformance("GET /orders", 1500, 0.002, 45, 110, 180),
                new OperationPerformance("POST /checkout", 420, 0.072, 250, 2300, 4200)
            ]
        };

        var config = new ObservabilityConfig
        {
            FailureRateAmberThreshold = 0.01,
            FailureRateRedThreshold = 0.05,
            LatencyAmberThresholdMs = 500,
            LatencyRedThresholdMs = 2000
        };

        var cut = RenderComponent<ObservabilityPerformance>(ps => ps
            .Add(p => p.Provider, provider)
            .Add(p => p.Range, TimeRange.Last24Hours)
            .Add(p => p.Config, config));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Operation latency and reliability", cut.Markup);
            Assert.Contains("Degraded", cut.Markup);
            Assert.Contains("POST /checkout", cut.Markup);
            Assert.Contains("obs-red", cut.Markup);
            Assert.Equal(1, provider.OperationPerformanceCallCount);
        });
    }

    [Fact]
    public async Task OnParametersSet_SameProviderAndRange_DoesNotReload()
    {
        var provider = new FakeObservabilityProvider
        {
            Operations = [new OperationPerformance("GET /items", 100, 0.001, 20, 40, 60)]
        };

        var cut = RenderComponent<ObservabilityPerformance>(ps => ps
            .Add(p => p.Provider, provider)
            .Add(p => p.Range, TimeRange.Last24Hours)
            .Add(p => p.Config, new ObservabilityConfig()));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("GET /items", cut.Markup);
            Assert.Equal(1, provider.OperationPerformanceCallCount);
        });

        cut.SetParametersAndRender(ps => ps
            .Add(p => p.Provider, provider)
            .Add(p => p.Range, TimeRange.Last24Hours)
            .Add(p => p.Config, new ObservabilityConfig { LatencyAmberThresholdMs = 300 }));

        cut.WaitForAssertion(() => Assert.Contains("GET /items", cut.Markup));
        await Task.Delay(150);
        Assert.Equal(1, provider.OperationPerformanceCallCount);
    }

    [Fact]
    public void ProviderError_ShowsErrorCallout()
    {
        var provider = new FakeObservabilityProvider
        {
            OperationPerformanceError = new InvalidOperationException("perf query failed")
        };

        var cut = RenderComponent<ObservabilityPerformance>(ps => ps
            .Add(p => p.Provider, provider)
            .Add(p => p.Range, TimeRange.Last24Hours)
            .Add(p => p.Config, new ObservabilityConfig()));

        cut.WaitForAssertion(() => Assert.Contains("perf query failed", cut.Markup));
    }

    private sealed class FakeObservabilityProvider : IObservabilityProvider
    {
        public int OperationPerformanceCallCount { get; private set; }

        public IReadOnlyList<OperationPerformance> Operations { get; set; } = [];
        public IReadOnlyList<LatencyDataPoint> Trend { get; set; } = [];
        public Exception? OperationPerformanceError { get; set; }

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

        public Task<IReadOnlyList<ExceptionGroup>> GetTopExceptionsAsync(TimeRange range, int top = 20, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExceptionGroup>>([]);

        public Task<IReadOnlyList<LogRow>> GetExceptionSamplesAsync(string exceptionType, TimeRange range, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LogRow>>([]);

        public Task<IReadOnlyList<OperationPerformance>> GetOperationPerformanceAsync(TimeRange range, CancellationToken ct = default)
        {
            OperationPerformanceCallCount++;
            if (OperationPerformanceError is not null)
            {
                throw OperationPerformanceError;
            }

            return Task.FromResult(Operations);
        }

        public Task<LogQueryResult> RunQueryAsync(string query, TimeRange range, int maxRows = 500, CancellationToken ct = default) =>
            Task.FromResult(new LogQueryResult([], [], TimeSpan.Zero, false));

        public Task<IReadOnlyList<AvailabilityResult>> GetAvailabilityAsync(TimeRange range, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AvailabilityResult>>([]);

        public Task<IReadOnlyList<LatencyDataPoint>> GetOperationLatencyTrendAsync(string operationName, TimeRange range, CancellationToken ct = default) =>
            Task.FromResult(Trend);

        public IReadOnlyList<QueryPreset> GetPresets() => [];
    }
}
