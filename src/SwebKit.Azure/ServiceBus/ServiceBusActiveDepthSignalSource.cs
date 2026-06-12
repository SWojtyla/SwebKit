using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Azure.ServiceBus;

public sealed class ServiceBusActiveDepthSignalSource : IAlertSignalSource
{
    private readonly IMonitoringConnectionPool _pool;
    private readonly ILogger<ServiceBusActiveDepthSignalSource> _logger;

    public AlertRuleSource Source => AlertRuleSource.ServiceBusActiveDepth;

    public ServiceBusActiveDepthSignalSource(IMonitoringConnectionPool pool, ILogger<ServiceBusActiveDepthSignalSource> logger)
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
            if (stats.ActiveMessageCount > p.MessageCountThreshold)
                return new AlertSignalResult(
                    AlertSignalStatus.Firing,
                    $"Active: {stats.ActiveMessageCount} messages on {p.EntityPath}",
                    $"Threshold: {p.MessageCountThreshold}");

            return new AlertSignalResult(AlertSignalStatus.Ok);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ServiceBusActiveDepthSignalSource error for rule {RuleId}", rule.Id);
            return new AlertSignalResult(AlertSignalStatus.Error, ex.Message);
        }
        // Note: do NOT dispose client — the pool owns its lifetime.
    }
}