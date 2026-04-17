namespace SwebKit.Core.Abstractions;

public interface IServiceBusWarmupCache
{
    void Store(Guid namespaceId, IServiceBusClient client);
    IServiceBusClient? TryGet(Guid namespaceId);
    void Invalidate();
}
