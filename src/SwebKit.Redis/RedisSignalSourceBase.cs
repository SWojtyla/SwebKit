using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Redis;

/// <summary>
/// Shared base for Redis alert signal sources. Centralizes the common params
/// validation, connection-pool client lookup, server-info fetch, cancellation
/// and error handling so derived sources supply only their distinct evaluation
/// logic.
/// </summary>
public abstract class RedisSignalSourceBase : IAlertSignalSource
{
    private readonly IMonitoringConnectionPool _pool;

    /// <summary>Logger for the concrete signal source, used by the shared error handler.</summary>
    protected ILogger Logger { get; }

    protected RedisSignalSourceBase(IMonitoringConnectionPool pool, ILogger logger)
    {
        _pool = pool;
        Logger = logger;
    }

    public abstract AlertRuleSource Source { get; }

    public async Task<AlertSignalResult> EvaluateAsync(MonitoringAlertRule rule, CancellationToken ct)
    {
        var p = rule.RedisAlertParams;
        if (p is null)
            return new AlertSignalResult(AlertSignalStatus.Skipped, "No Redis params");

        // Client is owned by the pool - do NOT dispose.
        var client = await _pool.GetRedisClientAsync(p.ConnectionAlias, ct).ConfigureAwait(false);
        if (client is null)
            return new AlertSignalResult(AlertSignalStatus.Skipped, $"Redis connection '{p.ConnectionAlias}' not found");

        try
        {
            var info = await client.GetServerInfoAsync(ct).ConfigureAwait(false);
            return Evaluate(p, info);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{SignalSource} error for rule {RuleId}", GetType().Name, rule.Id);
            return new AlertSignalResult(AlertSignalStatus.Error, ex.Message);
        }
    }

    /// <summary>
    /// Evaluates the fetched server info for the given rule params. Invoked inside
    /// the base class's cancellation/error handler, so implementations may throw freely.
    /// </summary>
    protected abstract AlertSignalResult Evaluate(RedisAlertParams p, RedisServerInfo info);
}
