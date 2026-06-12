using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Azure.ServiceBus;

public sealed class ServiceBusDlqSignalSource : IAlertSignalSource
{
    private readonly IMonitoringConnectionPool _pool;
    private readonly ILogger<ServiceBusDlqSignalSource> _logger;

    public AlertRuleSource Source => AlertRuleSource.ServiceBusDlqDepth;

    public ServiceBusDlqSignalSource(IMonitoringConnectionPool pool, ILogger<ServiceBusDlqSignalSource> logger)
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
            var stats = await client.GetEntityStatsAsync(p.EntityPath, ct);
            if (stats.DeadLetterMessageCount > p.MessageCountThreshold)
                return new AlertSignalResult(
                    AlertSignalStatus.Firing,
                    $"DLQ: {stats.DeadLetterMessageCount} messages on {p.EntityPath}",
                    $"Threshold: {p.MessageCountThreshold}");

            return new AlertSignalResult(AlertSignalStatus.Ok);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ServiceBusDlqSignalSource error for rule {RuleId}", rule.Id);
            return new AlertSignalResult(AlertSignalStatus.Error, ex.Message);
        }
        // Note: do NOT dispose client - the pool owns its lifetime.
    }
}