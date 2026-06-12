using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Kubernetes.AksClient;

public sealed class AksPodRestartRateSignalSource : IAlertSignalSource
{
    private readonly IMonitoringConnectionPool _pool;
    private readonly ILogger<AksPodRestartRateSignalSource> _logger;

    public AlertRuleSource Source => AlertRuleSource.AksPodRestartRate;

    public AksPodRestartRateSignalSource(IMonitoringConnectionPool pool, ILogger<AksPodRestartRateSignalSource> logger)
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
            var threshold = rule.AksPodParams?.RestartThreshold ?? 5;
            var exceeding = pods.Where(p => p.RestartCount >= threshold).ToList();
            if (exceeding.Count == 0)
                return new AlertSignalResult(AlertSignalStatus.Ok);
            var first = exceeding[0];
            return new AlertSignalResult(AlertSignalStatus.Firing,
                $"Pod {first.Name}: {first.RestartCount} restarts (threshold {threshold})",
                string.Join("; ", exceeding.Select(p => $"{p.Name}: {p.RestartCount} restarts")));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AksPodRestartRateSignalSource error for rule {RuleId}", rule.Id);
            return new AlertSignalResult(AlertSignalStatus.Error, ex.Message);
        }
        // Note: do NOT dispose client - the pool owns its lifetime.
    }
}