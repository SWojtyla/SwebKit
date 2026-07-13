using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Azure.ServiceBus;

public sealed class ServiceBusDeadSubscriptionSignalSource : IAlertSignalSource
{
    private readonly IMonitoringConnectionPool _pool;
    private readonly ILogger<ServiceBusDeadSubscriptionSignalSource> _logger;

    public AlertRuleSource Source => AlertRuleSource.ServiceBusDeadSubscription;

    public ServiceBusDeadSubscriptionSignalSource(IMonitoringConnectionPool pool, ILogger<ServiceBusDeadSubscriptionSignalSource> logger)
    {
        _pool = pool;
        _logger = logger;
    }

    public async Task<AlertSignalResult> EvaluateAsync(MonitoringAlertRule rule, CancellationToken ct)
    {
        var p = rule.ServiceBusParams;
        if (p is null)
            return new AlertSignalResult(AlertSignalStatus.Skipped, "No Service Bus params");

        var client = _pool.GetServiceBusClient(p.NamespaceConnectionAlias);
        if (client is null)
            return new AlertSignalResult(AlertSignalStatus.Skipped, $"Namespace '{p.NamespaceConnectionAlias}' not found or not configured");

        try
        {
            var stats = await client.GetEntityStatsAsync(p.EntityPath, ct).ConfigureAwait(false);
            if (stats.DeadLetterMessageCount > 0 && stats.ActiveMessageCount == 0)
                return new AlertSignalResult(
                    AlertSignalStatus.Firing,
                    $"Dead subscription on {p.EntityPath}: {stats.DeadLetterMessageCount} DLQ messages, 0 active",
                    "DLQ growing with no active messages — possible consumer outage");

            return new AlertSignalResult(AlertSignalStatus.Ok);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ServiceBusDeadSubscriptionSignalSource error for rule {RuleId}", rule.Id);
            return new AlertSignalResult(AlertSignalStatus.Error, ex.Message);
        }
        // Note: do NOT dispose client — the pool owns its lifetime.
    }
}
