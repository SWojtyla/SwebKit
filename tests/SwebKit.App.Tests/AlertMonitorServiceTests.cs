using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Tests;

/// <summary>
/// Tests for AlertMonitorService: cooldown, event emission, rule reload, and history ring buffer.
/// </summary>
public class AlertMonitorServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AlertMonitorService BuildService(
        out AppStateService appState,
        out TestWindowsNotificationService toast,
        out TestNotificationService notifications,
        IAlertRuleRepository? repository = null,
        IAlertSignalSource[]? sources = null)
    {
        var events = new AppEventBus(NullLogger<AppEventBus>.Instance);
        appState = new AppStateService(new ProfileRepository(), new UiStateRepository(), events);
        toast = new TestWindowsNotificationService();
        notifications = new TestNotificationService();

        return new AlertMonitorService(
            repository ?? new TestAlertRuleRepository(),
            sources ?? [],
            new NullMonitoringConnectionPool(),
            toast,
            notifications,
            new RecordingToastDiagnosticService(),
            appState,
            NullLogger<AlertMonitorService>.Instance);
    }

    // ── RecentAlerts ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RecentAlerts_IsEmpty_WhenNoRulesFire()
    {
        var svc = BuildService(out _, out _, out _);

        await svc.StartAsync();
        await Task.Delay(50); // let the first tick run

        Assert.Empty(svc.RecentAlerts);
        await svc.DisposeAsync();
    }

    // ── Alert emission ────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_EmitsAlertFired_WhenRuleConditionMet()
    {
        var rule = new MonitoringAlertRule
        {
            Id = "r1",
            Name = "Test",
            Source = AlertRuleSource.AksPodHealth,
            Enabled = true,
            IntervalSeconds = 10,
            CooldownMinutes = 1,
        };

        var source = new AlwaysFiringSignalSource(AlertRuleSource.AksPodHealth, "pod-abc: CrashLoop", "detail");
        var repo = new TestAlertRuleRepository(rule);

        var fired = new List<AlertFiredEvent>();
        var svc = BuildService(out _, out _, out _, repo, [source]);
        svc.AlertFired += evt => fired.Add(evt);

        await svc.StartAsync();
        await Task.Delay(200); // allow at least one tick

        await svc.DisposeAsync();

        Assert.NotEmpty(fired);
        Assert.Equal("Test", fired[0].RuleName);
        Assert.Equal(AlertRuleSource.AksPodHealth, fired[0].Source);
        Assert.Equal("pod-abc: CrashLoop", fired[0].Message);
    }

    [Fact]
    public async Task StartAsync_PopulatesRecentAlerts_WhenRuleFires()
    {
        var rule = new MonitoringAlertRule
        {
            Id = "r1",
            Name = "Redis memory",
            Source = AlertRuleSource.RedisMemoryUsage,
            Enabled = true,
            IntervalSeconds = 10,
            CooldownMinutes = 1,
        };

        var source = new AlwaysFiringSignalSource(AlertRuleSource.RedisMemoryUsage);
        var repo = new TestAlertRuleRepository(rule);
        var svc = BuildService(out _, out _, out _, repo, [source]);

        await svc.StartAsync();
        await Task.Delay(200);
        await svc.DisposeAsync();

        Assert.NotEmpty(svc.RecentAlerts);
    }

    // ── Toast fallback (DEC-4) ─────────────────────────────────────────────────

    [Fact]
    public async Task RuleFires_WhenToastUnavailable_RaisesInAppFallbackAndReportsDiagnostic()
    {
        var rule = new MonitoringAlertRule
        {
            Id = "r1",
            Name = "Pod down",
            Source = AlertRuleSource.AksPodHealth,
            Enabled = true,
            IntervalSeconds = 10,
            CooldownMinutes = 1,
            Severity = AlertSeverity.Warning,
        };

        var source = new AlwaysFiringSignalSource(AlertRuleSource.AksPodHealth, "pod-x: CrashLoop", "detail");
        var repo = new TestAlertRuleRepository(rule);

        var events = new AppEventBus(NullLogger<AppEventBus>.Instance);
        var appState = new AppStateService(new ProfileRepository(), new UiStateRepository(), events);
        // OS toast reports it cannot deliver — the in-app fallback + diagnostic must kick in.
        var toast = new TestWindowsNotificationService { NextResult = ToastDeliveryResult.NotAvailable("disabled") };
        var notifications = new RecordingNotificationService();
        var diagnostic = new RecordingToastDiagnosticService();

        var svc = new AlertMonitorService(
            repo, [source], new NullMonitoringConnectionPool(),
            toast, notifications, diagnostic, appState, NullLogger<AlertMonitorService>.Instance);

        await svc.StartAsync();
        await Task.Delay(200);
        await svc.DisposeAsync();

        Assert.NotEmpty(toast.ShownAlerts);                 // toast was attempted
        Assert.True(notifications.WarningCount >= 1);       // in-app baseline still raised (never silent)
        Assert.True(diagnostic.ReportCount >= 1);           // one-time diagnostic hint reported
        Assert.Equal("disabled", diagnostic.LastReason);
    }

    [Fact]
    public async Task RuleFires_WhenToastDelivered_DoesNotReportDiagnostic()
    {
        var rule = new MonitoringAlertRule
        {
            Id = "r1",
            Name = "Pod down",
            Source = AlertRuleSource.AksPodHealth,
            Enabled = true,
            IntervalSeconds = 10,
            CooldownMinutes = 1,
            Severity = AlertSeverity.Warning,
        };

        var source = new AlwaysFiringSignalSource(AlertRuleSource.AksPodHealth, "pod-x: CrashLoop", "detail");
        var repo = new TestAlertRuleRepository(rule);

        var events = new AppEventBus(NullLogger<AppEventBus>.Instance);
        var appState = new AppStateService(new ProfileRepository(), new UiStateRepository(), events);
        var toast = new TestWindowsNotificationService(); // default: ToastDeliveryResult.Shown()
        var notifications = new RecordingNotificationService();
        var diagnostic = new RecordingToastDiagnosticService();

        var svc = new AlertMonitorService(
            repo, [source], new NullMonitoringConnectionPool(),
            toast, notifications, diagnostic, appState, NullLogger<AlertMonitorService>.Instance);

        await svc.StartAsync();
        await Task.Delay(200);
        await svc.DisposeAsync();

        Assert.NotEmpty(toast.ShownAlerts);
        Assert.True(notifications.WarningCount >= 1);       // in-app baseline still raised
        Assert.Equal(0, diagnostic.ReportCount);            // no diagnostic when toast succeeds
    }

    // ── Cooldown ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cooldown_PreventsSubsequentFiringWithinWindow()
    {
        var rule = new MonitoringAlertRule
        {
            Id = "r1",
            Name = "DLQ",
            Source = AlertRuleSource.ServiceBusDlqDepth,
            Enabled = true,
            IntervalSeconds = 10, // evaluates every tick
            CooldownMinutes = 60, // very long cooldown
        };

        var source = new AlwaysFiringSignalSource(AlertRuleSource.ServiceBusDlqDepth);
        var repo = new TestAlertRuleRepository(rule);

        var fired = new List<AlertFiredEvent>();
        var svc = BuildService(out _, out _, out _, repo, [source]);
        svc.AlertFired += evt => fired.Add(evt);

        await svc.StartAsync();
        await Task.Delay(250); // multiple ticks
        await svc.DisposeAsync();

        // Should fire exactly once despite multiple evaluations
        Assert.Single(fired);
    }

    // ── Disabled rules ────────────────────────────────────────────────────────

    [Fact]
    public async Task DisabledRule_IsNotEvaluated()
    {
        var rule = new MonitoringAlertRule
        {
            Id = "r1",
            Name = "Disabled",
            Source = AlertRuleSource.AksPodHealth,
            Enabled = false,
        };

        var source = new AlwaysFiringSignalSource(AlertRuleSource.AksPodHealth);
        var repo = new TestAlertRuleRepository(rule);

        var fired = new List<AlertFiredEvent>();
        var svc = BuildService(out _, out _, out _, repo, [source]);
        svc.AlertFired += evt => fired.Add(evt);

        await svc.StartAsync();
        await Task.Delay(150);
        await svc.DisposeAsync();

        Assert.Empty(fired);
    }

    // ── Toast notification ────────────────────────────────────────────────────

    [Fact]
    public async Task Alert_CallsShowAlert_OnToastService()
    {
        var rule = new MonitoringAlertRule
        {
            Id = "r1",
            Source = AlertRuleSource.AksPodHealth,
            Enabled = true,
            IntervalSeconds = 10,
            CooldownMinutes = 60,
        };

        var source = new AlwaysFiringSignalSource(AlertRuleSource.AksPodHealth);
        var repo = new TestAlertRuleRepository(rule);
        var svc = BuildService(out _, out var toast, out _, repo, [source]);

        await svc.StartAsync();
        await Task.Delay(200);
        await svc.DisposeAsync();

        Assert.NotEmpty(toast.ShownAlerts);
    }

    // ── StopAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task StopAsync_SetsIsMonitoringFalse()
    {
        var svc = BuildService(out _, out _, out _);
        await svc.StartAsync();

        await svc.StopAsync();

        Assert.False(svc.IsMonitoring);
        await svc.DisposeAsync();
    }

    // ── ReloadRulesAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReloadRulesAsync_PicksUpNewRules()
    {
        var repo = new TestAlertRuleRepository();
        var source = new AlwaysFiringSignalSource(AlertRuleSource.AksPodHealth);
        var fired = new List<AlertFiredEvent>();
        var svc = BuildService(out _, out _, out _, repo, [source]);
        svc.AlertFired += evt => fired.Add(evt);

        await svc.StartAsync();
        await Task.Delay(50); // initial tick — no rules, nothing fires

        // Add a rule and reload — the next natural 10 s tick will pick it up.
        // But since the service starts immediately after reload, the due-time is reset to "now",
        // so the very next PeriodicTimer tick (≤10 s) will fire the rule.
        repo.Add(new MonitoringAlertRule
        {
            Id = "r1",
            Source = AlertRuleSource.AksPodHealth,
            Enabled = true,
            IntervalSeconds = 10,
            CooldownMinutes = 60,
        });
        await svc.ReloadRulesAsync();
        await Task.Delay(250); // wait for the next 10-second timer tick

        await svc.DisposeAsync();
        Assert.NotEmpty(fired);
    }

    // ── Unknown source ────────────────────────────────────────────────────────

    [Fact]
    public async Task UnknownSource_DoesNotThrow_AndProducesNoAlert()
    {
        var rule = new MonitoringAlertRule
        {
            Id = "r1",
            Source = AlertRuleSource.StorageBlobCount,
            Enabled = true,
            IntervalSeconds = 10,
        };

        // No signal source registered for StorageBlobCount
        var repo = new TestAlertRuleRepository(rule);
        var fired = new List<AlertFiredEvent>();
        var svc = BuildService(out _, out _, out _, repo, []);
        svc.AlertFired += evt => fired.Add(evt);

        await svc.StartAsync();
        await Task.Delay(150);
        await svc.DisposeAsync();

        Assert.Empty(fired);
    }

    // ── History ring buffer ───────────────────────────────────────────────────

    [Fact]
    public async Task RecentAlerts_CapAt200_OldestDropped()
    {
        // We use a rule with CooldownMinutes = 0 (effectively 0 — but our min is 1)
        // so the only way to get > 1 event from one rule is to set a low cooldown.
        // Instead we use 200 distinct rule IDs to fill the buffer.
        var rules = Enumerable.Range(0, 201)
            .Select(i => new MonitoringAlertRule
            {
                Id = $"r{i}",
                Name = $"Rule {i}",
                Source = AlertRuleSource.AksPodHealth,
                Enabled = true,
                IntervalSeconds = 10,
                CooldownMinutes = 0, // clamp to 0 so each rule fires at least once
            }).ToList();

        var source = new AlwaysFiringSignalSource(AlertRuleSource.AksPodHealth);
        var repo = new TestAlertRuleRepository(rules.ToArray());
        var svc = BuildService(out _, out _, out _, repo, [source]);

        await svc.StartAsync();
        await Task.Delay(300);
        await svc.DisposeAsync();

        Assert.True(svc.RecentAlerts.Count <= 200, $"Expected ≤ 200 but got {svc.RecentAlerts.Count}");
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class AlwaysFiringSignalSource(
        AlertRuleSource source,
        string message = "test alert",
        string detail = "test detail") : IAlertSignalSource
    {
        public AlertRuleSource Source => source;

        public Task<AlertSignalResult> EvaluateAsync(MonitoringAlertRule rule, CancellationToken ct)
            => Task.FromResult(new AlertSignalResult(AlertSignalStatus.Firing, message, detail));
    }

    private sealed class TestAlertRuleRepository(params MonitoringAlertRule[] initial) : IAlertRuleRepository
    {
        private readonly List<MonitoringAlertRule> _rules = [.. initial];

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

    private sealed class TestWindowsNotificationService : IWindowsNotificationService
    {
        public List<AlertFiredEvent> ShownAlerts { get; } = [];
        public ToastDeliveryResult NextResult { get; set; } = ToastDeliveryResult.Shown();
        public ToastCapability Capability { get; private set; } = ToastCapability.Available();
        public ToastCapability ProbeCapability() => Capability;
        public ToastDeliveryResult ShowPodAlert(PodHealthEvent evt) => NextResult;
        public ToastDeliveryResult ShowAlert(AlertFiredEvent evt)
        {
            ShownAlerts.Add(evt);
            return NextResult;
        }
    }

    private sealed class RecordingToastDiagnosticService : IToastDiagnosticService
    {
        public int ReportCount;
        public string? LastReason;
        public void ReportToastUnavailable(string? reason)
        {
            ReportCount++;
            LastReason = reason;
        }
    }

    private sealed class NullMonitoringConnectionPool : IMonitoringConnectionPool
    {
        public IAksClient? GetAksClient() => null;
        public IAksClient? GetAksClient(string? context) => null;
        public IServiceBusClient? GetServiceBusClient(string alias) => null;
        public ValueTask<IRedisClient?> GetRedisClientAsync(string displayName, CancellationToken ct = default) => default;
        public void InvalidateStaleConnections() { }
        public void EvictServiceBusClient(string alias) { }
        public ValueTask DisposeAsync() => default;
    }

    private sealed class TestNotificationService : INotificationService
    {
        public IReadOnlyList<Notification> All => [];
        public event Action? NotificationsChanged { add { } remove { } }
        public void ShowSuccess(string message, string? detail = null) { }
        public void ShowWarning(string message, string? detail = null) { }
        public void ShowError(string message, string? detail = null, Exception? ex = null) { }
        public void ShowInfo(string message, string? detail = null) { }
        public void Dismiss(Guid id) { }
        public void ClearAll() { }
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public int WarningCount;
        public int ErrorCount;
        public int InfoCount;
        public IReadOnlyList<Notification> All => [];
        public event Action? NotificationsChanged { add { } remove { } }
        public void ShowSuccess(string message, string? detail = null) { }
        public void ShowWarning(string message, string? detail = null) => Interlocked.Increment(ref WarningCount);
        public void ShowError(string message, string? detail = null, Exception? ex = null) => Interlocked.Increment(ref ErrorCount);
        public void ShowInfo(string message, string? detail = null) => Interlocked.Increment(ref InfoCount);
        public void Dismiss(Guid id) { }
        public void ClearAll() { }
    }
}
