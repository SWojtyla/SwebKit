using System.Reflection;
using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.Pages;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Observability;

namespace SwebKit.App.Tests;

public sealed class ObservabilityPageTests : TestContext
{
    private readonly FakeObservabilityProvider _provider = new();
    private readonly FakeObservabilityProviderFactory _providerFactory;

    public ObservabilityPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<int>("SwebKit.getBrowserTimezoneOffset").SetResult(0);
        var uiState = new UiStateRepository();

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
        {
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
        }

        Services.AddFluentUIComponents();

        var eventBus = new AppEventBus(NullLogger<AppEventBus>.Instance);
        var appState = new AppStateService(new ProfileRepository(), uiState, eventBus);
        appState.Config.ObservabilityConfig = new ObservabilityConfig
        {
            SelectedResourceId = "/subscriptions/test/resourceGroups/ops/providers/microsoft.insights/components/checkout-api",
            SelectedResourceName = "checkout-api"
        };

        _providerFactory = new FakeObservabilityProviderFactory(_provider);

        Services.AddSingleton<IAppEventBus>(eventBus);
        Services.AddSingleton(appState);
        Services.AddSingleton(uiState);
        Services.AddSingleton<IConnectionStateService, ConnectionStateService>();
        Services.AddSingleton<IObservabilityResourceDiscovery>(new EmptyObservabilityDiscovery());
        Services.AddSingleton<IObservabilityProviderFactory>(_providerFactory);
        Services.AddSingleton<INotificationService>(new NotificationService(uiState));
        Services.AddSingleton(new CommandRegistry(uiState));
        Services.AddSingleton<ISelectionContext>(new FakeSelectionContext());
        Services.AddSingleton<IGuidedKqlCompiler>(new GuidedKqlCompiler());
        Services.AddScoped<OperatorWorkspaceService>();
    }

    [Fact]
    public async Task DrillToLogs_UsesRenderReadyHandoff_AndRunsExactlyOnce()
    {
        var cut = RenderComponent<ObservabilityPage>();

        cut.WaitForAssertion(() => Assert.Equal(1, _providerFactory.CreateCalls));

        var drillToLogs = typeof(ObservabilityPage).GetMethod(
            "DrillToLogsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(drillToLogs);

        await cut.InvokeAsync(() => (Task)drillToLogs!.Invoke(cut.Instance, ["traces | take 1"])!);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, _provider.RunQueryCallCount);
            Assert.Equal("traces | take 1", _provider.LastQuery);
        });
    }

    private sealed class FakeObservabilityProviderFactory : IObservabilityProviderFactory
    {
        private readonly IObservabilityProvider _provider;

        public FakeObservabilityProviderFactory(IObservabilityProvider provider)
        {
            _provider = provider;
        }

        public int CreateCalls { get; private set; }

        public IObservabilityProvider Create(string resourceId, bool useDemoData)
        {
            CreateCalls++;
            return _provider;
        }
    }

    private sealed class FakeObservabilityProvider : IObservabilityProvider
    {
        public string ProviderType => "Fake";

        public int RunQueryCallCount { get; private set; }

        public string LastQuery { get; private set; } = string.Empty;

        public Task<OverviewMetrics> GetOverviewAsync(TimeRange range, CancellationToken ct = default) =>
            Task.FromResult(new OverviewMetrics(0, 0, 0, 0, 0, 0, [], []));

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
            return Task.FromResult(new LogQueryResult(["timestamp"], [new LogRow(new Dictionary<string, object?>
            {
                ["timestamp"] = DateTimeOffset.UtcNow
            })], TimeSpan.FromMilliseconds(12), false));
        }

        public Task<IReadOnlyList<AvailabilityResult>> GetAvailabilityAsync(TimeRange range, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AvailabilityResult>>([]);

        public Task<IReadOnlyList<LatencyDataPoint>> GetOperationLatencyTrendAsync(string operationName, TimeRange range, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LatencyDataPoint>>([]);

        public IReadOnlyList<QueryPreset> GetPresets() => [];
    }

    private sealed class EmptyObservabilityDiscovery : IObservabilityResourceDiscovery
    {
        public async IAsyncEnumerable<ObservabilityResourceInfo> DiscoverResourcesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeSelectionContext : ISelectionContext
    {
        public event Action? SelectionChanged;

        public void SetSelection(string area, object? selected)
        {
            SelectionChanged?.Invoke();
        }

        public T? GetSelection<T>(string area) where T : class => null;
    }
}