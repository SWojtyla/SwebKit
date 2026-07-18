using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Redis;

public sealed class RedisConnectedClientsSignalSource : RedisSignalSourceBase
{
    public override AlertRuleSource Source => AlertRuleSource.RedisConnectedClients;

    public RedisConnectedClientsSignalSource(IMonitoringConnectionPool pool, ILogger<RedisConnectedClientsSignalSource> logger)
        : base(pool, logger)
    {
    }

    protected override AlertSignalResult Evaluate(RedisAlertParams p, RedisServerInfo info)
    {
        var clients = info.ConnectedClients;

        if (clients < p.ClientCountLowerBound)
            return new AlertSignalResult(
                AlertSignalStatus.Firing,
                $"Redis connected clients: {clients} (minimum {p.ClientCountLowerBound})",
                "Connected clients dropped below threshold");

        return new AlertSignalResult(AlertSignalStatus.Ok);
    }
}
