using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Redis;

public sealed class RedisMemorySignalSource : IAlertSignalSource
{
    private readonly IMonitoringConnectionPool _pool;
    private readonly ILogger<RedisMemorySignalSource> _logger;

    public AlertRuleSource Source => AlertRuleSource.RedisMemoryUsage;

    public RedisMemorySignalSource(IMonitoringConnectionPool pool, ILogger<RedisMemorySignalSource> logger)
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

            if (info.MaxMemoryBytes <= 0)
                return new AlertSignalResult(AlertSignalStatus.Ok); // unlimited - skip

            var usedPct = (double)info.UsedMemoryBytes / info.MaxMemoryBytes * 100.0;
            if (usedPct >= p.MemoryUsageThresholdPercent)
                return new AlertSignalResult(
                    AlertSignalStatus.Firing,
                    $"Redis memory: {usedPct:F1}% used (threshold {p.MemoryUsageThresholdPercent:F0}%)",
                    $"Used: {info.UsedMemoryBytes / 1_048_576} MB / {info.MaxMemoryBytes / 1_048_576} MB");

            return new AlertSignalResult(AlertSignalStatus.Ok);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RedisMemorySignalSource error for rule {RuleId}", rule.Id);
            return new AlertSignalResult(AlertSignalStatus.Error, ex.Message);
        }
    }
}