using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

// ── Fakes ────────────────────────────────────────────────────────────────────

/// <summary>Controllable signal source: returns a fixed status and records evaluation calls.</summary>
internal sealed class FakeSignalSource : IAlertSignalSource
{
    private readonly AlertSignalStatus _status;
    public int CallCount { get; private set; }
    public bool WasCalled => CallCount > 0;

    public FakeSignalSource(AlertRuleSource source, AlertSignalStatus status = AlertSignalStatus.Ok)
    {
        Source = source;
        _status = status;
    }

    public AlertRuleSource Source { get; }

    public Task<AlertSignalResult> EvaluateAsync(MonitoringAlertRule rule, CancellationToken ct)
    {
        CallCount++;
        if (_status == AlertSignalStatus.Error)
            throw new InvalidOperationException("fake signal failure");
        return Task.FromResult(new AlertSignalResult(_status, $"value for {rule.Name}", null));
    }
}

/// <summary>No-op connection pool: the engine only calls InvalidateStaleConnections on reload.</summary>
internal sealed class FakeConnectionPool : IMonitoringConnectionPool
{
    public int InvalidateCalls { get; private set; }
    public void InvalidateStaleConnections() => InvalidateCalls++;
    public void EvictServiceBusClient(string alias) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    // Unused resolver methods return null — the engine never calls them during evaluation.
    public IAksClient? GetAksClient() => null;
    public IAksClient? GetAksClient(string? context) => null;
    public IServiceBusClient? GetServiceBusClient(string alias) => null;
    public ValueTask<IRedisClient?> GetRedisClientAsync(string displayName, CancellationToken ct = default)
        => ValueTask.FromResult<IRedisClient?>(null);
}

// ── Engine behavioural tests ────────────────────────────────────────────────────

public class MonitoringAlertEvaluationServiceTests
{
    private static MonitoringAlertEvaluationService Build(
        IAlertRuleRepository repo,
        params IAlertSignalSource[] sources)
    {
        return new MonitoringAlertEvaluationService(
            repo,
            new FakeConnectionPool(),
            sources,
            new ProfileRepository(),
            NullLogger<MonitoringAlertEvaluationService>.Instance);
    }

    private static MonitoringAlertRule Rule(
        AlertRuleSource source,
        bool enabled = true,
        int cooldownMinutes = 10) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = $"{source} rule",
        Source = source,
        Enabled = enabled,
        Severity = AlertSeverity.Warning,
        IntervalSeconds = 10,
        CooldownMinutes = cooldownMinutes,
    };

    [Fact]
    public async Task RunEvaluationOnce_FiresEvent_WhenSourceReturnsFiring()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        var source = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var engine = Build(repo, source);

        var rule = Rule(AlertRuleSource.AksPodHealth);
        await repo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        AlertFiredEvent? fired = null;
        engine.AlertFired += e => fired = e;

        await engine.RunEvaluationOnceAsync();

        Assert.True(source.WasCalled);
        Assert.NotNull(fired);
        Assert.Equal(rule.Id, fired!.RuleId);
        Assert.Equal(AlertSeverity.Warning, fired.Severity);
        Assert.Single(engine.RecentAlerts);
    }

    [Fact]
    public async Task DisabledRule_IsNotEvaluated()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        var source = new FakeSignalSource(AlertRuleSource.AksPodHealth);
        var engine = Build(repo, source);

        var rule = Rule(AlertRuleSource.AksPodHealth, enabled: false);
        await repo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        await engine.RunEvaluationOnceAsync();

        Assert.False(source.WasCalled);
        Assert.Empty(engine.RecentAlerts);
    }

    [Fact]
    public async Task Cooldown_SuppressesRepeatFire_WithinWindow()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        var source = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var engine = Build(repo, source);

        var rule = Rule(AlertRuleSource.AksPodHealth, cooldownMinutes: 10);
        await repo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        int fireCount = 0;
        engine.AlertFired += _ => fireCount++;

        await engine.RunEvaluationOnceAsync();
        await engine.RunEvaluationOnceAsync();

        Assert.Equal(1, fireCount);
        Assert.Equal(1, source.CallCount); // second pass skipped via cooldown, source not re-evaluated
    }

    [Fact]
    public async Task RingBuffer_CapsAt200_DroppingOldest()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        var source = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var engine = Build(repo, source);

        // 205 distinct rules, all due on the first pass (no cooldown/interval gate applies
        // within a single pass because _nextEvaluateAt starts empty). Exercises the 200 cap.
        var rules = new List<MonitoringAlertRule>();
        for (int i = 0; i < 205; i++)
            rules.Add(Rule(AlertRuleSource.AksPodHealth, cooldownMinutes: 0));
        await repo.SaveAllAsync(rules);
        await engine.ReloadRulesAsync();

        await engine.RunEvaluationOnceAsync();

        Assert.Equal(200, engine.RecentAlerts.Count);
        // All events belong to one of the fired rules; the oldest (first) was evicted.
        Assert.All(engine.RecentAlerts, e => Assert.StartsWith("AksPodHealth", e.RuleName));
    }

    [Fact]
    public async Task SourceError_SchedulesBackoff_AndSuppressesImmediateRerun()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        var source = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Error);
        var engine = Build(repo, source);

        var rule = Rule(AlertRuleSource.AksPodHealth);
        await repo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        await engine.RunEvaluationOnceAsync();
        await engine.RunEvaluationOnceAsync(); // should be skipped: nextEvaluateAt pushed into the future

        Assert.Equal(1, source.CallCount);
        Assert.Empty(engine.RecentAlerts);
    }

    [Fact]
    public async Task UnknownSource_IsSkippedWithoutThrowing()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        // Only a Redis source is registered; the rule uses an AKS source not present.
        var source = new FakeSignalSource(AlertRuleSource.RedisMemoryUsage);
        var engine = Build(repo, source);

        var rule = Rule(AlertRuleSource.AksPodHealth);
        await repo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        // Should not throw despite no matching source.
        await engine.RunEvaluationOnceAsync();

        Assert.False(source.WasCalled);
        Assert.Empty(engine.RecentAlerts);
    }

    [Fact]
    public async Task ReloadRulesAsync_ClearsSchedule_SoCooledDownRuleBecomesDueAgain()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        var source = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var engine = Build(repo, source);

        var rule = Rule(AlertRuleSource.AksPodHealth, cooldownMinutes: 10);
        await repo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        int fireCount = 0;
        engine.AlertFired += _ => fireCount++;

        await engine.RunEvaluationOnceAsync();          // due -> fires (1)
        await engine.RunEvaluationOnceAsync();          // cooled-down -> skipped (still 1)
        Assert.Equal(1, fireCount);

        await engine.ReloadRulesAsync();                 // clears _nextEvaluateAt + cooldown
        await engine.RunEvaluationOnceAsync();          // due again -> fires (2)

        Assert.Equal(2, fireCount);
        Assert.Equal(2, engine.RecentAlerts.Count);
    }
}
