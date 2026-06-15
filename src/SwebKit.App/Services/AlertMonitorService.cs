using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Services;

public sealed class AlertMonitorService : IAlertMonitorService
{
    private const int RingBufferCapacity = 200;
    private const int MaxConcurrentEvaluations = 4;
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);
    private const double MaxBackoffSeconds = 600; // 10 minutes

    private readonly IAlertRuleRepository _repository;
    private readonly IReadOnlyDictionary<AlertRuleSource, IAlertSignalSource> _sources;
    private readonly IMonitoringConnectionPool _pool;
    private readonly IWindowsNotificationService _toast;
    private readonly INotificationService _notifications;
    private readonly AppStateService _appState;
    private readonly ILogger<AlertMonitorService> _logger;

    private readonly SemaphoreSlim _concurrencyLimit = new(MaxConcurrentEvaluations, MaxConcurrentEvaluations);
    private readonly object _historyLock = new();
    private readonly List<AlertFiredEvent> _recentAlerts = new(RingBufferCapacity + 1);
    private readonly Dictionary<string, DateTimeOffset> _cooldowns = [];
    private readonly Dictionary<string, DateTimeOffset> _nextEvaluateAt = [];

    /// Tracks consecutive error/skipped evaluations per rule ID for exponential backoff.
    private readonly Dictionary<string, int> _consecutiveFailures = [];

    private List<MonitoringAlertRule> _rules = [];
    private volatile bool _isMonitoring;
    private CancellationTokenSource? _cts;
    private PeriodicTimer? _timer;
    private Task? _loopTask;
    private bool _disposed;

    public bool IsMonitoring => _isMonitoring;

    public IReadOnlyList<AlertFiredEvent> RecentAlerts
    {
        get
        {
            lock (_historyLock)
            {
                return _recentAlerts.ToList();
            }
        }
    }

    public event Action<AlertFiredEvent>? AlertFired;
    public event Action<AlertEvaluatedEvent>? EvaluationCompleted;

    public AlertMonitorService(
        IAlertRuleRepository repository,
        IEnumerable<IAlertSignalSource> sources,
        IMonitoringConnectionPool pool,
        IWindowsNotificationService toast,
        INotificationService notifications,
        AppStateService appState,
        ILogger<AlertMonitorService> logger)
    {
        _repository = repository;
        _sources = sources.ToDictionary(s => s.Source);
        _pool = pool;
        _toast = toast;
        _notifications = notifications;
        _appState = appState;
        _logger = logger;
        _appState.Initialized += OnAppInitialized;
    }

    private void OnAppInitialized() => _ = StartAsync();

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_isMonitoring)
            return;

        _rules = [.. await _repository.GetAllAsync()];
        _isMonitoring = true;
        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TickInterval);
        _loopTask = Task.Run(() => PollingLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _timer?.Dispose();
        _timer = null;

        if (_loopTask is not null)
        {
            try { await _loopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            _loopTask = null;
        }

        _isMonitoring = false;
    }

    public async Task ReloadRulesAsync()
    {
        _pool.InvalidateStaleConnections(); // pick up any credential changes
        _rules = [.. await _repository.GetAllAsync()];
        lock (_historyLock)
        {
            _nextEvaluateAt.Clear();
            _consecutiveFailures.Clear(); // reset backoff so new config gets a clean start
        }
        // Next natural 10-second tick will evaluate the updated rule set.
        // Do NOT evaluate immediately here — that would block the UI save path.
    }

    private async Task PollingLoopAsync(CancellationToken ct)
    {
        try
        {
            await EvaluateDueRulesAsync(ct);
            while (await _timer!.WaitForNextTickAsync(ct))
                await EvaluateDueRulesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("AlertMonitorService polling loop cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in AlertMonitorService polling loop.");
        }
    }

    private Task EvaluateDueRulesAsync(CancellationToken ct)
    {
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

        if (!_sources.TryGetValue(rule.Source, out var source))
        {
            _logger.LogWarning("No signal source registered for {Source}", rule.Source);
            // Unknown source — check again in 2 min to avoid log spam
            lock (_historyLock) { _nextEvaluateAt[rule.Id] = now.AddSeconds(120); }
            return;
        }

        await _concurrencyLimit.WaitAsync(ct);
        try
        {
            AlertSignalResult result;
            try
            {
                result = await source.EvaluateAsync(rule, ct);
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

            // OK or Firing — reset backoff and schedule at normal interval
            lock (_historyLock)
            {
                _consecutiveFailures.Remove(rule.Id);
                _nextEvaluateAt[rule.Id] = now.AddSeconds(intervalSeconds);
            }

            if (result.Status != AlertSignalStatus.Firing)
                return;

            // Cooldown check
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
                _appState.Config.Name);

            lock (_historyLock)
            {
                if (_recentAlerts.Count >= RingBufferCapacity)
                    _recentAlerts.RemoveAt(0);
                _recentAlerts.Add(evt);
            }

            try { AlertFired?.Invoke(evt); }
            catch (Exception ex) { _logger.LogWarning(ex, "AlertFired handler threw for rule {RuleId}", rule.Id); }

            try { _toast.ShowAlert(evt); }
            catch (Exception ex) { _logger.LogWarning(ex, "ShowAlert threw for rule {RuleId}", rule.Id); }

            if (rule.Severity == AlertSeverity.Critical)
                _notifications.ShowError(evt.RuleName, evt.Message);
            else
                _notifications.ShowWarning(evt.RuleName, evt.Message);
        }
        finally
        {
            _concurrencyLimit.Release();
        }
    }

    /// <summary>
    /// Applies exponential backoff: base × 2^(failures−1), capped at 10 minutes.
    /// A rule with Error/Skipped responses is polled progressively less often to avoid
    /// hammering unavailable services when the app is running in the background.
    /// </summary>
    private void ScheduleWithBackoff(string ruleId, DateTimeOffset now, double baseIntervalSeconds)
    {
        lock (_historyLock)
        {
            var failures = _consecutiveFailures.GetValueOrDefault(ruleId) + 1;
            _consecutiveFailures[ruleId] = failures;
            var backoffSeconds = Math.Min(baseIntervalSeconds * Math.Pow(2, failures - 1), MaxBackoffSeconds);
            _nextEvaluateAt[ruleId] = now.AddSeconds(backoffSeconds);
            _logger.LogDebug(
                "Rule {RuleId} backed off to {Backoff:F0}s (failure #{Count})",
                ruleId, backoffSeconds, failures);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _appState.Initialized -= OnAppInitialized;
        await StopAsync();
        _cts?.Dispose();
        _concurrencyLimit.Dispose();
    }
}
