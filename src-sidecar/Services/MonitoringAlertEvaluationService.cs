using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Server-side monitoring evaluation engine for the Tauri/React stack. Ports the MAUI
/// <c>AlertMonitorService</c> algorithm (due-scheduling, cooldown, exponential backoff, bounded
/// concurrency, in-memory ring buffer) as a hosted <see cref="BackgroundService"/>. Fired events
/// are surfaced via <see cref="AlertFired"/> so the SSE endpoint can push them to the UI.
/// </summary>
public sealed class MonitoringAlertEvaluationService : BackgroundService
{
    private const int RingBufferCapacity = 200;
    private const int MaxConcurrentEvaluations = 4;
    private static readonly TimeSpan DefaultTickInterval = TimeSpan.FromSeconds(10);
    private const double MaxBackoffSeconds = 600; // 10 minutes

    private readonly IAlertRuleRepository _repository;
    private readonly IMonitoringConnectionPool _pool;
    private readonly IEnumerable<IAlertSignalSource> _sources;
    private readonly ProfileRepository _profile;
    private readonly IMonitoringSilenceRepository _silences;
    private readonly IAlertHistoryRepository _history;
    private readonly ILogger<MonitoringAlertEvaluationService> _logger;

    private readonly SemaphoreSlim _concurrencyLimit = new(MaxConcurrentEvaluations, MaxConcurrentEvaluations);
    private readonly object _historyLock = new();
    private readonly List<AlertFiredEvent> _recentAlerts = new(RingBufferCapacity + 1);
    private readonly Dictionary<string, DateTimeOffset> _cooldowns = new();
    private readonly Dictionary<string, DateTimeOffset> _nextEvaluateAt = new();
    private readonly Dictionary<string, int> _consecutiveFailures = new();
    /// <summary>Rule ids with a firing that hasn't seen an <see cref="AlertSignalStatus.Ok"/>
    /// evaluation since — an Ok tick for one of these is the incident's recovery signal and
    /// emits <see cref="AlertResolved"/> plus a durable <see cref="AlertHistoryKind.Resolved"/>
    /// row. Error/Skipped evaluations prove nothing about the underlying condition, so they
    /// leave the incident open.</summary>
    private readonly HashSet<string> _openIncidents = new(StringComparer.Ordinal);

    private Dictionary<AlertRuleSource, IAlertSignalSource> _sourceMap = new();
    private List<MonitoringAlertRule> _rules = [];
    private volatile bool _started;
    private PeriodicTimer? _timer;

    public event Action<AlertFiredEvent>? AlertFired;
    /// <summary>Raised when a rule with an open incident evaluates Ok — the recovery signal the
    /// volatile ring buffer never had. Streams to the UI as <c>alertResolved</c>.</summary>
    public event Action<AlertResolvedEvent>? AlertResolved;
    public event Action<AlertEvaluatedEvent>? EvaluationCompleted;

    public IReadOnlyList<AlertFiredEvent> RecentAlerts
    {
        get
        {
            lock (_historyLock)
                return _recentAlerts.ToList();
        }
    }

    public MonitoringAlertEvaluationService(
        IAlertRuleRepository repository,
        IMonitoringConnectionPool pool,
        IEnumerable<IAlertSignalSource> sources,
        ProfileRepository profile,
        IMonitoringSilenceRepository silences,
        IAlertHistoryRepository history,
        ILogger<MonitoringAlertEvaluationService> logger)
    {
        _repository = repository;
        _pool = pool;
        _sources = sources;
        _profile = profile;
        _silences = silences;
        _history = history;
        _logger = logger;
    }

    public async Task ReloadRulesAsync()
    {
        _sourceMap = _sources.ToDictionary(s => s.Source);
        _pool.InvalidateStaleConnections();
        _rules = [.. await _repository.GetAllAsync().ConfigureAwait(false)];
        lock (_historyLock)
        {
            _nextEvaluateAt.Clear();
            _consecutiveFailures.Clear();
            _cooldowns.Clear();
            // Incidents survive a reload — the condition is still real regardless of rule edits —
            // but an incident for a rule that no longer exists can never resolve.
            _openIncidents.RemoveWhere(id => _rules.All(r => r.Id != id));
        }
    }

    /// <summary>Runs a single due-rules evaluation pass. Exposed for deterministic unit testing;
    /// the hosted loop calls this on every timer tick.</summary>
    public Task RunEvaluationOnceAsync(CancellationToken ct = default)
    {
        _started = true;
        return EvaluateDueRulesAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _sourceMap = _sources.ToDictionary(s => s.Source);
        _rules = [.. await _repository.GetAllAsync().ConfigureAwait(false)];
        _started = true;
        _timer = new PeriodicTimer(DefaultTickInterval);

        try
        {
            await EvaluateDueRulesAsync(stoppingToken).ConfigureAwait(false);
            while (await _timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                await EvaluateDueRulesAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Monitoring evaluation loop cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in monitoring evaluation loop.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task EvaluateDueRulesAsync(CancellationToken ct)
    {
        if (!_started)
            return Task.CompletedTask;

        var now = DateTimeOffset.UtcNow;
        List<MonitoringAlertRule> dueRules;
        lock (_historyLock)
        {
            dueRules = _rules
                .Where(r => r.Enabled)
                .Where(r =>
                {
                    if (_nextEvaluateAt.TryGetValue(r.Id, out var next))
                        return now >= next;
                    return true;
                })
                .ToList();
        }

        var tasks = dueRules.Select(rule => EvaluateRuleAsync(rule, now, ct)).ToList();
        return Task.WhenAll(tasks);
    }

    private async Task EvaluateRuleAsync(MonitoringAlertRule rule, DateTimeOffset now, CancellationToken ct)
    {
        var intervalSeconds = Math.Max(10, rule.IntervalSeconds);

        if (!_sourceMap.TryGetValue(rule.Source, out var source))
        {
            _logger.LogWarning("No signal source registered for {Source}", rule.Source);
            lock (_historyLock) { _nextEvaluateAt[rule.Id] = now.AddSeconds(120); }
            return;
        }

        await _concurrencyLimit.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            AlertSignalResult result;
            try
            {
                result = await source.EvaluateAsync(rule, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Signal source {Source} threw for rule {RuleId}", rule.Source, rule.Id);
                rule.LastEvaluatedAt = now;
                EvaluationCompleted?.Invoke(new AlertEvaluatedEvent(rule.Id, AlertSignalStatus.Error, now, ex.Message));
                ScheduleWithBackoff(rule.Id, now, intervalSeconds);
                return;
            }

            rule.LastEvaluatedAt = now;
            EvaluationCompleted?.Invoke(new AlertEvaluatedEvent(rule.Id, result.Status, now, result.Message));

            if (result.Status is AlertSignalStatus.Error or AlertSignalStatus.Skipped)
            {
                ScheduleWithBackoff(rule.Id, now, intervalSeconds);
                return;
            }

            lock (_historyLock)
            {
                _consecutiveFailures.Remove(rule.Id);
                _nextEvaluateAt[rule.Id] = now.AddSeconds(intervalSeconds);
            }

            if (result.Status == AlertSignalStatus.Ok)
                await ResolveOpenIncidentAsync(rule, now, result.Message).ConfigureAwait(false);

            if (result.Status != AlertSignalStatus.Firing)
                return;

            bool inCooldown;
            lock (_historyLock)
            {
                inCooldown = _cooldowns.TryGetValue(rule.Id, out var cooldownExpiry) && now < cooldownExpiry;
            }

            if (inCooldown)
                return;

            lock (_historyLock)
            {
                _cooldowns[rule.Id] = now.AddMinutes(rule.CooldownMinutes);
            }

            rule.LastFiredAt = now;

            // Silence/mute suppression happens at the firing stage only — after the cooldown is
            // claimed, never as an evaluation status. Emitting Skipped here would trigger the
            // failure backoff instead of the normal interval, and a suppressed firing must still
            // be recorded so the audit trail shows what would have fired.
            var (suppressed, suppressedBy) = await ResolveSuppressionAsync(rule, now).ConfigureAwait(false);

            var evt = new AlertFiredEvent(
                rule.Id,
                rule.Name,
                rule.Source,
                rule.Severity,
                result.Message ?? rule.Name,
                result.Detail ?? string.Empty,
                now,
                _profile.GetProfileData().Config.Name ?? "default",
                suppressed,
                suppressedBy);

            lock (_historyLock)
            {
                if (_recentAlerts.Count >= RingBufferCapacity)
                    _recentAlerts.RemoveAt(0);
                _recentAlerts.Add(evt);
                _openIncidents.Add(rule.Id);
            }

            await AppendHistoryAsync(new AlertHistoryEntry
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                Source = rule.Source,
                Severity = rule.Severity,
                Kind = suppressed ? AlertHistoryKind.Suppressed : AlertHistoryKind.Fired,
                At = now,
                Message = suppressedBy is not null ? $"{evt.Message} (suppressed: {suppressedBy})" : evt.Message,
            }).ConfigureAwait(false);

            try { AlertFired?.Invoke(evt); }
            catch (Exception ex) { _logger.LogWarning(ex, "AlertFired handler threw for rule {RuleId}", rule.Id); }
        }
        finally
        {
            _concurrencyLimit.Release();
        }
    }

    /// <summary>Decides whether this firing is suppressed, and by what. Runs at the firing stage
    /// only — a muted rule still evaluates normally (its status dot and backoff stay honest) and
    /// still produces a recorded firing, flagged <see cref="AlertFiredEvent.Suppressed"/> so
    /// subscribers can downgrade it. Per-rule <see cref="MonitoringAlertRule.MutedUntil"/> is
    /// checked before shared <see cref="MonitoringSilence"/> windows. The store is read lazily at
    /// firing time — a post-cooldown firing is rare enough that a fresh read beats caching +
    /// invalidation — and a store failure fails open: a corrupted file must never eat alerts.</summary>
    private async Task<(bool Suppressed, string? By)> ResolveSuppressionAsync(MonitoringAlertRule rule, DateTimeOffset now)
    {
        if (rule.MutedUntil is { } mutedUntil && mutedUntil > now)
            return (true, $"rule muted until {mutedUntil:u}");

        try
        {
            var silence = (await _silences.GetAllAsync().ConfigureAwait(false))
                .FirstOrDefault(s => s.AppliesTo(rule.Id, now));
            if (silence is not null)
                return (true, string.IsNullOrWhiteSpace(silence.Reason) ? $"silence {silence.Id}" : silence.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load monitoring silences — firing rule {RuleId} unsuppressed", rule.Id);
        }

        return (false, null);
    }

    /// <summary>Persists one durable history row. History is audit data — a persistence failure
    /// must never take the firing path (or the notification after it) down with it.</summary>
    private async Task AppendHistoryAsync(AlertHistoryEntry entry)
    {
        try
        {
            await _history.AppendAsync(entry).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist {Kind} alert-history entry for rule {RuleId}", entry.Kind, entry.RuleId);
        }
    }

    /// <summary>Emits the recovery signal for a rule whose incident is open: a durable
    /// <see cref="AlertHistoryKind.Resolved"/> row plus the <c>alertResolved</c> event that lets
    /// the UI clear the firing state. No-op for a rule that never fired — an Ok evaluation on a
    /// healthy rule is not a resolution.</summary>
    private async Task ResolveOpenIncidentAsync(MonitoringAlertRule rule, DateTimeOffset now, string? message)
    {
        bool wasOpen;
        lock (_historyLock)
            wasOpen = _openIncidents.Remove(rule.Id);
        if (!wasOpen)
            return;

        await AppendHistoryAsync(new AlertHistoryEntry
        {
            RuleId = rule.Id,
            RuleName = rule.Name,
            Source = rule.Source,
            Severity = rule.Severity,
            Kind = AlertHistoryKind.Resolved,
            At = now,
            Message = message ?? $"{rule.Name} recovered",
        }).ConfigureAwait(false);

        var evt = new AlertResolvedEvent(rule.Id, rule.Name, rule.Source, rule.Severity, now, message);
        try { AlertResolved?.Invoke(evt); }
        catch (Exception ex) { _logger.LogWarning(ex, "AlertResolved handler threw for rule {RuleId}", rule.Id); }
    }

    private void ScheduleWithBackoff(string ruleId, DateTimeOffset now, double baseIntervalSeconds)
    {
        lock (_historyLock)
        {
            var failures = _consecutiveFailures.GetValueOrDefault(ruleId) + 1;
            _consecutiveFailures[ruleId] = failures;
            var backoffSeconds = Math.Min(baseIntervalSeconds * Math.Pow(2, failures - 1), MaxBackoffSeconds);
            _nextEvaluateAt[ruleId] = now.AddSeconds(backoffSeconds);
            _logger.LogDebug("Rule {RuleId} backed off to {Backoff:F0}s (failure #{Count})", ruleId, backoffSeconds, failures);
        }
    }
}
