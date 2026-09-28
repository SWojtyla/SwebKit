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
        // Finished pods (Job/CronJob leftovers waiting for cleanup, phase Succeeded)
        // are not workload health — a namespace that just ran its batch schedule must
        // not score worse for it. Failed pods still count: that IS the signal.
        var live = pods.Where(p => p.Phase != "Succeeded").ToList();
        if (live.Count == 0)
            return new AlertSignalResult(AlertSignalStatus.Ok);

        var threshold = rule.AksPodParams?.HealthScoreThreshold ?? 0.25;
        var notReady = live.Count(p => p.ReadyContainers < p.TotalContainers || p.Phase != "Running");
        var score = (double)notReady / live.Count;
        if (score < threshold)
            return new AlertSignalResult(AlertSignalStatus.Ok);

        var pct = (int)(score * 100);
        return new AlertSignalResult(AlertSignalStatus.Firing,
            $"{pct}% of pods not ready (threshold {(int)(threshold * 100)}%)",
            $"{notReady}/{live.Count} pods not ready");
    }
}
