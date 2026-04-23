using SwebKit.Core.Abstractions;

namespace SwebKit.App.Services;

public sealed class ServiceBusWarmupCache : IServiceBusWarmupCache
{
    private readonly Dictionary<Guid, IServiceBusClient> _clients = [];

    public void Store(Guid namespaceId, IServiceBusClient client) =>
        _clients[namespaceId] = client;

    public IServiceBusClient? TryGet(Guid namespaceId) =>
        _clients.TryGetValue(namespaceId, out var c) ? c : null;

    public void Invalidate() => _clients.Clear();
}
