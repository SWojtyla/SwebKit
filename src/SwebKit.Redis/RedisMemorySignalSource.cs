using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Redis;

public sealed class RedisMemorySignalSource : RedisSignalSourceBase
{
    public override AlertRuleSource Source => AlertRuleSource.RedisMemoryUsage;

    public RedisMemorySignalSource(IMonitoringConnectionPool pool, ILogger<RedisMemorySignalSource> logger)
        : base(pool, logger)
    {
    }

    protected override AlertSignalResult Evaluate(RedisAlertParams p, RedisServerInfo info)
    {
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
}
