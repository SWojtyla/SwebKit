using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Azure.ServiceBus;

public sealed class ServiceBusDeadSubscriptionSignalSource : ServiceBusSignalSourceBase
{
    public override AlertRuleSource Source => AlertRuleSource.ServiceBusDeadSubscription;

    public ServiceBusDeadSubscriptionSignalSource(IMonitoringConnectionPool pool, ILogger<ServiceBusDeadSubscriptionSignalSource> logger)
        : base(pool, logger)
    {
    }

    protected override AlertSignalResult Evaluate(ServiceBusAlertParams p, SbEntityStats stats)
    {
        if (stats.DeadLetterMessageCount > 0 && stats.ActiveMessageCount == 0)
            return new AlertSignalResult(
                AlertSignalStatus.Firing,
                $"Dead subscription on {p.EntityPath}: {stats.DeadLetterMessageCount} DLQ messages, 0 active",
                "DLQ growing with no active messages — possible consumer outage");

        return new AlertSignalResult(AlertSignalStatus.Ok);
    }
}
