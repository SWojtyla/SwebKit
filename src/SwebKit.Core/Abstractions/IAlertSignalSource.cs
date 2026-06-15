using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IAlertSignalSource
{
    AlertRuleSource Source { get; }
    Task<AlertSignalResult> EvaluateAsync(MonitoringAlertRule rule, CancellationToken ct);
}
