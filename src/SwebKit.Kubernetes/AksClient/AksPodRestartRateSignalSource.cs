using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Kubernetes.AksClient;

public sealed class AksPodRestartRateSignalSource : PodSignalSourceBase
{
    public override AlertRuleSource Source => AlertRuleSource.AksPodRestartRate;

    public AksPodRestartRateSignalSource(IMonitoringConnectionPool pool, ILogger<AksPodRestartRateSignalSource> logger)
        : base(pool, logger)
    {
    }

    protected override AlertSignalResult Evaluate(MonitoringAlertRule rule, string ns, IReadOnlyList<PodInfo> pods)
    {
        var threshold = rule.AksPodParams?.RestartThreshold ?? 5;
        var exceeding = pods.Where(p => p.RestartCount >= threshold).ToList();
        if (exceeding.Count == 0)
            return new AlertSignalResult(AlertSignalStatus.Ok);
        var first = exceeding[0];
        return new AlertSignalResult(AlertSignalStatus.Firing,
            $"Pod {first.Name}: {first.RestartCount} restarts (threshold {threshold})",
            string.Join("; ", exceeding.Select(p => $"{p.Name}: {p.RestartCount} restarts")));
    }
}