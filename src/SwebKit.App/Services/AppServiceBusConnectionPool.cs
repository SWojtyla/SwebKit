using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.App.Services;

public sealed class AppServiceBusConnectionPool(
    IMonitoringConnectionPool monitoringPool,
    AppStateService appState) : IServiceBusConnectionPool
{
    public IServiceBusClient GetOrCreate(ServiceBusNamespace ns) =>
        monitoringPool.GetServiceBusClient(ns.Alias) ??
        throw new InvalidOperationException($"Service Bus client is unavailable for namespace '{ns.Alias}'.");

    public void Evict(string namespaceId)
    {
        var ns = appState.ServiceBusNamespaces.FirstOrDefault(candidate =>
            string.Equals(candidate.Id.ToString(), namespaceId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Id.ToString("N"), namespaceId, StringComparison.OrdinalIgnoreCase));
        if (ns is not null) monitoringPool.EvictServiceBusClient(ns.Alias);
    }

    public void InvalidateAll()
    {
        foreach (var ns in appState.ServiceBusNamespaces)
            monitoringPool.EvictServiceBusClient(ns.Alias);
    }
}
