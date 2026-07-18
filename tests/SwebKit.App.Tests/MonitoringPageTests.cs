using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.App.Components.Monitoring;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Tests;

/// <summary>
/// Tests for <see cref="MonitoringPage"/> — the alert-rules page (`/monitoring`). Exercises rule
/// loading, the monitoring start/stop toggle, and rule delete/toggle wiring through fakes for
/// <see cref="IAlertMonitorService"/> and <see cref="IAlertRuleRepository"/>. Rendering the "Add
/// rule" drawer also requires <see cref="IAksClientFactory"/> and
/// <see cref="IMonitoringConnectionPool"/> to be registered, since <c>AlertRuleDrawer</c> injects
/// them.
/// </summary>
public sealed class MonitoringPageTests : TestContext
{
    private readonly FakeAlertMonitorService _monitor = new();
    private readonly FakeAlertRuleRepository _rules = new();

    public MonitoringPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
        {
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
        }

        Services.AddSingleton<IAlertMonitorService>(_monitor);
        Services.AddSingleton<IAlertRuleRepository>(_rules);
        Services.AddSingleton<INotificationService>(new NotificationService(new UiStateRepository()));
        Services.AddSingleton(new AppStateService(new ProfileRepository(), new UiStateRepository(),
            new AppEventBus(NullLogger<AppEventBus>.Instance)));
        Services.AddSingleton<IAksClientFactory>(new FakeAksClientFactory());
        Services.AddSingleton<IMonitoringConnectionPool>(new FakeMonitoringConnectionPool());
    }

    [Fact]
    public void OnInitialized_LoadsRulesFromRepository_AndShowsRuleCount()
    {
        _rules.Add(new MonitoringAlertRule { Id = "r1", Name = "Prod pod health", Source = AlertRuleSource.AksPodHealth });
        _rules.Add(new MonitoringAlertRule { Id = "r2", Name = "DLQ depth", Source = AlertRuleSource.ServiceBusDlqDepth });

        var cut = RenderComponent<MonitoringPage>();

        Assert.Contains("2 rules", cut.Markup);
        Assert.Contains("Prod pod health", cut.Markup);
        Assert.Contains("DLQ depth", cut.Markup);
    }

    [Fact]
    public void NoRules_ShowsSingularRuleCount_ForOneRule()
    {
        _rules.Add(new MonitoringAlertRule { Id = "r1", Name = "Solo rule", Source = AlertRuleSource.AksPodHealth });

        var cut = RenderComponent<MonitoringPage>();

        Assert.Contains("1 rule", cut.Markup);
        Assert.DoesNotContain("1 rules", cut.Markup);
    }

    [Fact]
    public void ToggleButton_WhenNotMonitoring_StartsMonitoring()
    {
        var cut = RenderComponent<MonitoringPage>();

        Assert.Contains("Monitoring paused", cut.Markup);

        cut.Find("button.monitoring-toggle-btn").Click();

        Assert.Equal(1, _monitor.StartCallCount);
    }

    [Fact]
    public void ToggleButton_WhenMonitoring_StopsMonitoring()
    {
        _monitor.IsMonitoring = true;

        var cut = RenderComponent<MonitoringPage>();

        Assert.Contains("Monitoring active", cut.Markup);

        cut.Find("button.monitoring-toggle-btn").Click();

        Assert.Equal(1, _monitor.StopCallCount);
    }

    [Fact]
    public void AlertFired_AddsToHistory_AndIncrementsFiringBadge()
    {
        _rules.Add(new MonitoringAlertRule { Id = "r1", Name = "Prod pod health", Source = AlertRuleSource.AksPodHealth });

        var cut = RenderComponent<MonitoringPage>();

        cut.InvokeAsync(() => _monitor.RaiseAlertFired(new AlertFiredEvent(
            RuleId: "r1",
            RuleName: "Prod pod health",
            Source: AlertRuleSource.AksPodHealth,
            Severity: AlertSeverity.Warning,
            Message: "3 pods unhealthy",
            Detail: "detail",
            FiredAt: DateTimeOffset.UtcNow,
            ProfileName: "default")));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("1 firing", cut.Markup);
            Assert.Contains("3 pods unhealthy", cut.Markup);
        });
    }

    [Fact]
    public void DeletingRule_RemovesItFromRepositoryAndPage()
    {
        _rules.Add(new MonitoringAlertRule { Id = "r1", Name = "Prod pod health", Source = AlertRuleSource.AksPodHealth });

        var cut = RenderComponent<MonitoringPage>();
        Assert.Contains("Prod pod health", cut.Markup);

        cut.Find("button.alert-rule-row__action-btn--danger").Click();
        cut.Find("button.alert-rule-row__action-btn--danger").Click(); // confirm

        cut.WaitForAssertion(() => Assert.DoesNotContain("Prod pod health", cut.Markup));
        Assert.Empty(_rules.All);
    }

    [Fact]
    public void OpenCreateDrawer_RendersAlertRuleDrawer()
    {
        var cut = RenderComponent<MonitoringPage>();

        cut.Find("button.page-header-action-btn:not(.monitoring-toggle-btn)").Click();

        Assert.NotEmpty(cut.FindAll(".alert-rule-modal"));
    }

    private sealed class FakeAlertMonitorService : IAlertMonitorService
    {
        public bool IsMonitoring { get; set; }
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public IReadOnlyList<AlertFiredEvent> RecentAlerts { get; set; } = [];

        public event Action<AlertFiredEvent>? AlertFired;
        public event Action<AlertEvaluatedEvent>? EvaluationCompleted;

        public Task StartAsync(CancellationToken ct = default)
        {
            StartCallCount++;
            IsMonitoring = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCallCount++;
            IsMonitoring = false;
            return Task.CompletedTask;
        }

        public void RaiseAlertFired(AlertFiredEvent evt) => AlertFired?.Invoke(evt);
        public void RaiseEvaluationCompleted(AlertEvaluatedEvent evt) => EvaluationCompleted?.Invoke(evt);

        public ValueTask DisposeAsync() => default;
    }

    private sealed class FakeAlertRuleRepository : IAlertRuleRepository
    {
        private readonly List<MonitoringAlertRule> _rules = [];

        public IReadOnlyList<MonitoringAlertRule> All => _rules;

        public void Add(MonitoringAlertRule rule) => _rules.Add(rule);

        public Task<IReadOnlyList<MonitoringAlertRule>> GetAllAsync()
            => Task.FromResult<IReadOnlyList<MonitoringAlertRule>>(_rules.ToList());

        public Task SaveAllAsync(IReadOnlyList<MonitoringAlertRule> rules)
        {
            _rules.Clear();
            _rules.AddRange(rules);
            return Task.CompletedTask;
        }

        public Task<MonitoringAlertRule?> GetByIdAsync(string id)
            => Task.FromResult(_rules.FirstOrDefault(r => r.Id == id));

        public Task UpsertAsync(MonitoringAlertRule rule)
        {
            var idx = _rules.FindIndex(r => r.Id == rule.Id);
            if (idx >= 0) _rules[idx] = rule;
            else _rules.Add(rule);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id)
        {
            _rules.RemoveAll(r => r.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAksClientFactory : IAksClientFactory
    {
        public IAksClient Create(string? context, string? kubeconfigPath) =>
            throw new InvalidOperationException("Factory should not be called in this test.");
    }

    private sealed class FakeMonitoringConnectionPool : IMonitoringConnectionPool
    {
        public IAksClient? GetAksClient() => null;
        public IAksClient? GetAksClient(string? context) => null;
        public IServiceBusClient? GetServiceBusClient(string alias) => null;
        public ValueTask<IRedisClient?> GetRedisClientAsync(string displayName, CancellationToken ct = default) => default;
        public void InvalidateStaleConnections() { }
        public void EvictServiceBusClient(string alias) { }
        public ValueTask DisposeAsync() => default;
    }
}
