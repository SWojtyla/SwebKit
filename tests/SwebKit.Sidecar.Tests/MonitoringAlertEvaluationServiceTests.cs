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
    private AlertSignalStatus _status;
    public int CallCount { get; private set; }
    public bool WasCalled => CallCount > 0;

    public FakeSignalSource(AlertRuleSource source, AlertSignalStatus status = AlertSignalStatus.Ok)
    {
        Source = source;
        _status = status;
    }

    /// <summary>Lets a test flip the outcome mid-run (e.g. Firing → Ok → Firing) to simulate an
    /// alert recovering and firing a second incident.</summary>
    public AlertSignalStatus Status { get => _status; set => _status = value; }

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
    public void EvictAksClients() { }
    public void EvictRedisClient(string key) { }
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
        params IAlertSignalSource[] sources) =>
        Build(repo, new InMemoryMonitoringSilenceRepository(), new InMemoryAlertHistoryRepository(), sources);

    private static MonitoringAlertEvaluationService Build(
        IAlertRuleRepository repo,
        IMonitoringSilenceRepository silences,
        InMemoryAlertHistoryRepository history,
        params IAlertSignalSource[] sources)
    {
        return new MonitoringAlertEvaluationService(
            repo,
            new FakeConnectionPool(),
            sources,
            new ProfileRepository(),
            silences,
            history,
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
    public async Task EvaluationCompleted_CarriesStatusAndMessage_ForEachOutcome()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        var okSource = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Ok);
        var errSource = new FakeSignalSource(AlertRuleSource.ServiceBusDlqDepth, AlertSignalStatus.Error);
        var engine = Build(repo, okSource, errSource);

        var okRule = Rule(AlertRuleSource.AksPodHealth);
        var errRule = Rule(AlertRuleSource.ServiceBusDlqDepth);
        await repo.SaveAllAsync([okRule, errRule]);
        await engine.ReloadRulesAsync();

        var events = new List<AlertEvaluatedEvent>();
        engine.EvaluationCompleted += e => events.Add(e);

        await engine.RunEvaluationOnceAsync();

        var ok = Assert.Single(events, e => e.RuleId == okRule.Id);
        Assert.Equal(AlertSignalStatus.Ok, ok.Status);
        Assert.Equal($"value for {okRule.Name}", ok.Message);

        // FakeSignalSource throws for Error — the engine reports the exception message.
        var err = Assert.Single(events, e => e.RuleId == errRule.Id);
        Assert.Equal(AlertSignalStatus.Error, err.Status);
        Assert.Equal("fake signal failure", err.Message);
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

    // ── Silence windows + per-rule mute (monitoring-closed-loop item 3) ─────────

    [Fact]
    public async Task Firing_UnderAnActiveGlobalSilence_StillEmitsAlertFired_Suppressed()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        var silences = new InMemoryMonitoringSilenceRepository();
        var history = new InMemoryAlertHistoryRepository();
        var source = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var engine = Build(repo, silences, history, source);

        var rule = Rule(AlertRuleSource.AksPodHealth);
        await repo.UpsertAsync(rule);
        var now = DateTimeOffset.UtcNow;
        await silences.UpsertAsync(new MonitoringSilence
        {
            StartUtc = now.AddMinutes(-5),
            EndUtc = now.AddHours(1),
            Reason = "weekend deploy freeze",
            RuleIds = null, // covers every rule
        });
        await engine.ReloadRulesAsync();

        AlertFiredEvent? fired = null;
        engine.AlertFired += e => fired = e;
        await engine.RunEvaluationOnceAsync();

        // Suppression happens at the firing stage — the event still emits, flagged.
        Assert.NotNull(fired);
        Assert.True(fired!.Suppressed);
        Assert.Equal("weekend deploy freeze", fired.SuppressedBy);
        Assert.Single(engine.RecentAlerts);

        // ...and the durable history records it as Suppressed, not Fired.
        var entry = Assert.Single(history.Entries);
        Assert.Equal(AlertHistoryKind.Suppressed, entry.Kind);
        Assert.Contains("weekend deploy freeze", entry.Message);
    }

    [Fact]
    public async Task Firing_UnderARuleScopedSilence_OnlySuppressesMatchingRules()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        var silences = new InMemoryMonitoringSilenceRepository();
        var history = new InMemoryAlertHistoryRepository();
        var source = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var engine = Build(repo, silences, history, source);

        var covered = Rule(AlertRuleSource.AksPodHealth);
        var uncovered = Rule(AlertRuleSource.AksPodHealth);
        await repo.SaveAllAsync([covered, uncovered]);
        var now = DateTimeOffset.UtcNow;
        await silences.UpsertAsync(new MonitoringSilence
        {
            StartUtc = now.AddMinutes(-5),
            EndUtc = now.AddHours(1),
            Reason = "scoped window",
            RuleIds = [covered.Id],
        });
        await engine.ReloadRulesAsync();

        var fired = new List<AlertFiredEvent>();
        engine.AlertFired += e => fired.Add(e);
        await engine.RunEvaluationOnceAsync();

        var suppressed = Assert.Single(fired, e => e.RuleId == covered.Id);
        Assert.True(suppressed.Suppressed);
        var unsuppressed = Assert.Single(fired, e => e.RuleId == uncovered.Id);
        Assert.False(unsuppressed.Suppressed);
        Assert.Null(unsuppressed.SuppressedBy);
    }

    [Fact]
    public async Task MutedUntil_SuppressesFiring_AndExpiresBackToNormal()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        var history = new InMemoryAlertHistoryRepository();
        var source = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var engine = Build(repo, new InMemoryMonitoringSilenceRepository(), history, source);

        var rule = Rule(AlertRuleSource.AksPodHealth);
        rule.MutedUntil = DateTimeOffset.UtcNow.AddHours(1);
        await repo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        AlertFiredEvent? fired = null;
        engine.AlertFired += e => fired = e;
        await engine.RunEvaluationOnceAsync();

        Assert.NotNull(fired);
        Assert.True(fired!.Suppressed);
        Assert.Contains("rule muted until", fired.SuppressedBy);

        // Cooldown is claimed even for a suppressed firing — a second pass must not re-emit.
        fired = null;
        await engine.RunEvaluationOnceAsync();
        Assert.Null(fired);
        Assert.Single(engine.RecentAlerts);

        // A mute timestamp in the past is no mute at all — the next firing is normal.
        // Reload clears the claimed cooldown so the expiry path itself is what we observe.
        rule.MutedUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
        await repo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();
        await engine.RunEvaluationOnceAsync();

        Assert.NotNull(fired);
        Assert.False(fired!.Suppressed);
        Assert.Null(fired.SuppressedBy);
    }

    [Fact]
    public async Task ExpiredSilence_DoesNotSuppress_TheNextFiring()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        var silences = new InMemoryMonitoringSilenceRepository();
        var source = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var engine = Build(repo, silences, new InMemoryAlertHistoryRepository(), source);

        var rule = Rule(AlertRuleSource.AksPodHealth);
        await repo.UpsertAsync(rule);
        var now = DateTimeOffset.UtcNow;
        await silences.UpsertAsync(new MonitoringSilence
        {
            StartUtc = now.AddHours(-2),
            EndUtc = now.AddHours(-1), // already over
            Reason = "past window",
        });
        await engine.ReloadRulesAsync();

        AlertFiredEvent? fired = null;
        engine.AlertFired += e => fired = e;
        await engine.RunEvaluationOnceAsync();

        Assert.NotNull(fired);
        Assert.False(fired!.Suppressed);
    }

    // ── Durable history + recovery signal (monitoring-closed-loop 4a) ───────────

    [Fact]
    public async Task Firing_AppendsAFiredEntry_ToDurableHistory()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        var history = new InMemoryAlertHistoryRepository();
        var source = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var engine = Build(repo, new InMemoryMonitoringSilenceRepository(), history, source);

        var rule = Rule(AlertRuleSource.AksPodHealth);
        await repo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        await engine.RunEvaluationOnceAsync();

        var entry = Assert.Single(history.Entries);
        Assert.Equal(AlertHistoryKind.Fired, entry.Kind);
        Assert.Equal(rule.Id, entry.RuleId);
        Assert.Equal(rule.Name, entry.RuleName);
    }

    [Fact]
    public async Task OkEvaluation_AfterAFiring_EmitsAlertResolved_AndAppendsAResolvedEntry()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        var history = new InMemoryAlertHistoryRepository();
        var source = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var engine = Build(repo, new InMemoryMonitoringSilenceRepository(), history, source);

        var rule = Rule(AlertRuleSource.AksPodHealth);
        await repo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        AlertResolvedEvent? resolved = null;
        engine.AlertResolved += e => resolved = e;

        await engine.RunEvaluationOnceAsync(); // fires — incident opens
        Assert.Null(resolved);

        // Recovery: flip the source to Ok. Reload clears the schedule so the rule is due.
        source.Status = AlertSignalStatus.Ok;
        await engine.ReloadRulesAsync();
        await engine.RunEvaluationOnceAsync();

        Assert.NotNull(resolved);
        Assert.Equal(rule.Id, resolved!.RuleId);
        Assert.Equal(rule.Name, resolved.RuleName);

        Assert.Equal(
            [AlertHistoryKind.Resolved, AlertHistoryKind.Fired],
            history.Entries.Select(e => e.Kind).ToList()); // newest first

        // A healthy rule that never fired does not emit a resolution.
        resolved = null;
        await engine.ReloadRulesAsync();
        await engine.RunEvaluationOnceAsync();
        Assert.Null(resolved);
    }

    [Fact]
    public async Task SuppressedFiring_OpensTheIncident_SoRecoveryStillResolvesIt()
    {
        using var _ = new AppDataSandbox();
        var repo = new AlertRuleRepository();
        var history = new InMemoryAlertHistoryRepository();
        var source = new FakeSignalSource(AlertRuleSource.AksPodHealth, AlertSignalStatus.Firing);
        var engine = Build(repo, new InMemoryMonitoringSilenceRepository(), history, source);

        // A suppressed firing is still a real incident — the underlying condition fired, the
        // notification just didn't go out. Its recovery still produces a Resolved entry.
        var rule = Rule(AlertRuleSource.AksPodHealth);
        rule.MutedUntil = DateTimeOffset.UtcNow.AddHours(1);
        await repo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();

        AlertResolvedEvent? resolved = null;
        engine.AlertResolved += e => resolved = e;

        await engine.RunEvaluationOnceAsync(); // suppressed firing — incident opens

        source.Status = AlertSignalStatus.Ok;
        rule.MutedUntil = null; // mute lapsed between the firing and the recovery
        await repo.UpsertAsync(rule);
        await engine.ReloadRulesAsync();
        await engine.RunEvaluationOnceAsync();

        Assert.NotNull(resolved);
        Assert.Equal(
            [AlertHistoryKind.Resolved, AlertHistoryKind.Suppressed],
            history.Entries.Select(e => e.Kind).ToList());
    }
}
