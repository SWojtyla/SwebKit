using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Redis;

public sealed class RedisConnectedClientsSignalSource : IAlertSignalSource
{
    private readonly IMonitoringConnectionPool _pool;
    private readonly ILogger<RedisConnectedClientsSignalSource> _logger;

    public AlertRuleSource Source => AlertRuleSource.RedisConnectedClients;

    public RedisConnectedClientsSignalSource(IMonitoringConnectionPool pool, ILogger<RedisConnectedClientsSignalSource> logger)
    {
        _pool = pool;
        _logger = logger;
    }

    public async Task<AlertSignalResult> EvaluateAsync(MonitoringAlertRule rule, CancellationToken ct)
    {
        var p = rule.RedisAlertParams;
        if (p is null)
            return new AlertSignalResult(AlertSignalStatus.Skipped, "No Redis params");

        // Client is owned by the pool - do NOT dispose.
        var client = await _pool.GetRedisClientAsync(p.ConnectionAlias, ct);
        if (client is null)
            return new AlertSignalResult(AlertSignalStatus.Skipped, $"Redis connection '{p.ConnectionAlias}' not found");

        try
        {
            var info = await client.GetServerInfoAsync(ct);
            var clients = info.ConnectedClients;

            if (clients < p.ClientCountLowerBound)
                return new AlertSignalResult(
                    AlertSignalStatus.Firing,
                    $"Redis connected clients: {clients} (minimum {p.ClientCountLowerBound})",
                    "Connected clients dropped below threshold");

            return new AlertSignalResult(AlertSignalStatus.Ok);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RedisConnectedClientsSignalSource error for rule {RuleId}", rule.Id);
            return new AlertSignalResult(AlertSignalStatus.Error, ex.Message);
        }
    }
}