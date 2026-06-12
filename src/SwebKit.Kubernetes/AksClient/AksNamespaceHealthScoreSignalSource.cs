using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Kubernetes.AksClient;

public sealed class AksNamespaceHealthScoreSignalSource : IAlertSignalSource
{
    private readonly IMonitoringConnectionPool _pool;
    private readonly ILogger<AksNamespaceHealthScoreSignalSource> _logger;

    public AlertRuleSource Source => AlertRuleSource.AksNamespaceHealthScore;

    public AksNamespaceHealthScoreSignalSource(IMonitoringConnectionPool pool, ILogger<AksNamespaceHealthScoreSignalSource> logger)
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
            if (pods.Count == 0)
                return new AlertSignalResult(AlertSignalStatus.Ok);

            var threshold = rule.AksPodParams?.HealthScoreThreshold ?? 0.25;
            var notReady = pods.Count(p => p.ReadyContainers < p.TotalContainers || p.Phase != "Running");
            var score = (double)notReady / pods.Count;
            if (score < threshold)
                return new AlertSignalResult(AlertSignalStatus.Ok);

            var pct = (int)(score * 100);
            return new AlertSignalResult(AlertSignalStatus.Firing,
                $"{pct}% of pods not ready (threshold {(int)(threshold * 100)}%)",
                $"{notReady}/{pods.Count} pods not ready");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AksNamespaceHealthScoreSignalSource error for rule {RuleId}", rule.Id);
            return new AlertSignalResult(AlertSignalStatus.Error, ex.Message);
        }
        // Note: do NOT dispose client — the pool owns its lifetime.
    }
}
