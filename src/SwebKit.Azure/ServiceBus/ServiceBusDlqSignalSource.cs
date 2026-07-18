using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Azure.ServiceBus;

public sealed class ServiceBusDlqSignalSource : ServiceBusSignalSourceBase
{
    public override AlertRuleSource Source => AlertRuleSource.ServiceBusDlqDepth;

    public ServiceBusDlqSignalSource(IMonitoringConnectionPool pool, ILogger<ServiceBusDlqSignalSource> logger)
        : base(pool, logger)
    {
    }

    protected override AlertSignalResult Evaluate(ServiceBusAlertParams p, SbEntityStats stats)
    {
        if (stats.DeadLetterMessageCount > p.MessageCountThreshold)
            return new AlertSignalResult(
                AlertSignalStatus.Firing,
                $"DLQ: {stats.DeadLetterMessageCount} messages on {p.EntityPath}",
                $"Threshold: {p.MessageCountThreshold}");

        return new AlertSignalResult(AlertSignalStatus.Ok);
    }
}
