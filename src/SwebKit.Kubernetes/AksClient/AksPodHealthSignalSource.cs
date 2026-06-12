using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Kubernetes.AksClient;

public sealed class AksPodHealthSignalSource : IAlertSignalSource
{
    private readonly IMonitoringConnectionPool _pool;
    private readonly ILogger<AksPodHealthSignalSource> _logger;
    private readonly Dictionary<string, Dictionary<string, PodSnapshot>?> _snapshots = [];
    private readonly object _lock = new();

    public AlertRuleSource Source => AlertRuleSource.AksPodHealth;

    public AksPodHealthSignalSource(IMonitoringConnectionPool pool, ILogger<AksPodHealthSignalSource> logger)
    {
        _pool = pool;
        _logger = logger;
    }

    public async Task<AlertSignalResult> EvaluateAsync(MonitoringAlertRule rule, CancellationToken ct)
    {
        var client = _pool.GetAksClient();
        if (client is null)
            return new AlertSignalResult(AlertSignalStatus.Skipped, "AKS not configured");

        var ns = rule.AksPodParams?.Namespace ?? string.Empty;
        try
        {
            var pods = await client.GetPodsAsync(ns, null, ct);

            Dictionary<string, PodSnapshot>? existing;
            lock (_lock) { _snapshots.TryGetValue(rule.Id, out existing); }

            var diffs = PodHealthDiffer.Diff(ns, existing, pods, new Dictionary<string, DateTimeOffset>(), DateTimeOffset.UtcNow);

            lock (_lock)
            {
                _snapshots[rule.Id] = pods.ToDictionary(
                    p => p.Name,
                    p => new PodSnapshot(p.Phase, p.ReadyContainers, p.TotalContainers, p.RestartCount));
            }

            if (diffs.Count == 0)
                return new AlertSignalResult(AlertSignalStatus.Ok);

            var first = diffs[0];
            var msg = diffs.Count == 1
                ? $"Pod {first.PodName}: {first.EventType}"
                : $"{diffs.Count} pods affected in {(string.IsNullOrEmpty(ns) ? "all namespaces" : ns)}";
            return new AlertSignalResult(AlertSignalStatus.Firing, msg,
                string.Join("; ", diffs.Select(d => $"{d.PodName}: {d.EventType}")));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AksPodHealthSignalSource error for rule {RuleId}", rule.Id);
            return new AlertSignalResult(AlertSignalStatus.Error, ex.Message);
        }
        // Note: do NOT dispose client — the pool owns its lifetime.
    }
}
