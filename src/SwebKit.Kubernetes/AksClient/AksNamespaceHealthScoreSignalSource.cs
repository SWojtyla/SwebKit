using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Kubernetes.AksClient;

public sealed class AksNamespaceHealthScoreSignalSource : PodSignalSourceBase
{
    public override AlertRuleSource Source => AlertRuleSource.AksNamespaceHealthScore;

    public AksNamespaceHealthScoreSignalSource(IMonitoringConnectionPool pool, ILogger<AksNamespaceHealthScoreSignalSource> logger)
        : base(pool, logger)
    {
    }

    protected override AlertSignalResult Evaluate(MonitoringAlertRule rule, string ns, IReadOnlyList<PodInfo> pods)
    {
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
}