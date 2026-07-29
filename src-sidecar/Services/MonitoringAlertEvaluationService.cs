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
    private readonly ILogger<MonitoringAlertEvaluationService> _logger;

    private readonly SemaphoreSlim _concurrencyLimit = new(MaxConcurrentEvaluations, MaxConcurrentEvaluations);
    private readonly object _historyLock = new();
    private readonly List<AlertFiredEvent> _recentAlerts = new(RingBufferCapacity + 1);
    private readonly Dictionary<string, DateTimeOffset> _cooldowns = new();
    private readonly Dictionary<string, DateTimeOffset> _nextEvaluateAt = new();
    private readonly Dictionary<string, int> _consecutiveFailures = new();

    private Dictionary<AlertRuleSource, IAlertSignalSource> _sourceMap = new();
    private List<MonitoringAlertRule> _rules = [];
    private volatile bool _started;
    private PeriodicTimer? _timer;

    public event Action<AlertFiredEvent>? AlertFired;
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
        ILogger<MonitoringAlertEvaluationService> logger)
    {
        _repository = repository;
        _pool = pool;
        _sources = sources;
        _profile = profile;
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
                EvaluationCompleted?.Invoke(new AlertEvaluatedEvent(rule.Id, AlertSignalStatus.Error, now));
                ScheduleWithBackoff(rule.Id, now, intervalSeconds);
                return;
            }

            rule.LastEvaluatedAt = now;
            EvaluationCompleted?.Invoke(new AlertEvaluatedEvent(rule.Id, result.Status, now));

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

            var evt = new AlertFiredEvent(
                rule.Id,
                rule.Name,
                rule.Source,
                rule.Severity,
                result.Message ?? rule.Name,
                result.Detail ?? string.Empty,
                now,
                _profile.GetProfileData().Config.Name ?? "default");

            lock (_historyLock)
            {
                if (_recentAlerts.Count >= RingBufferCapacity)
                    _recentAlerts.RemoveAt(0);
                _recentAlerts.Add(evt);
            }

            try { AlertFired?.Invoke(evt); }
            catch (Exception ex) { _logger.LogWarning(ex, "AlertFired handler threw for rule {RuleId}", rule.Id); }
        }
        finally
        {
            _concurrencyLimit.Release();
        }
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
