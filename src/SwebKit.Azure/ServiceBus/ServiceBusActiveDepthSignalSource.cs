using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Azure.ServiceBus;

public sealed class ServiceBusActiveDepthSignalSource : ServiceBusSignalSourceBase
{
    public override AlertRuleSource Source => AlertRuleSource.ServiceBusActiveDepth;

    public ServiceBusActiveDepthSignalSource(IMonitoringConnectionPool pool, ILogger<ServiceBusActiveDepthSignalSource> logger)
        : base(pool, logger)
    {
    }

    protected override AlertSignalResult Evaluate(ServiceBusAlertParams p, SbEntityStats stats)
    {
        if (stats.ActiveMessageCount > p.MessageCountThreshold)
            return new AlertSignalResult(
                AlertSignalStatus.Firing,
                $"Active: {stats.ActiveMessageCount} messages on {p.EntityPath}",
                $"Threshold: {p.MessageCountThreshold}");

        return new AlertSignalResult(AlertSignalStatus.Ok);
    }
}
