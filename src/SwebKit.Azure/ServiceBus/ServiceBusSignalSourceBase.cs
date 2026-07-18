using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Azure.ServiceBus;

/// <summary>
/// Shared base for Service Bus alert signal sources. Centralizes the common
/// params validation, connection-pool client lookup, entity-stats fetch,
/// cancellation and error handling so derived sources supply only their
/// distinct evaluation logic.
/// </summary>
public abstract class ServiceBusSignalSourceBase : IAlertSignalSource
{
    private readonly IMonitoringConnectionPool _pool;

    /// <summary>Logger for the concrete signal source, used by the shared error handler.</summary>
    protected ILogger Logger { get; }

    protected ServiceBusSignalSourceBase(IMonitoringConnectionPool pool, ILogger logger)
    {
        _pool = pool;
        Logger = logger;
    }

    public abstract AlertRuleSource Source { get; }

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
            return Evaluate(p, stats);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{SignalSource} error for rule {RuleId}", GetType().Name, rule.Id);
            return new AlertSignalResult(AlertSignalStatus.Error, ex.Message);
        }
        // Note: do NOT dispose client — the pool owns its lifetime.
    }

    /// <summary>
    /// Evaluates the fetched entity stats for the given rule params. Invoked inside
    /// the base class's cancellation/error handler, so implementations may throw freely.
    /// </summary>
    protected abstract AlertSignalResult Evaluate(ServiceBusAlertParams p, SbEntityStats stats);
}
