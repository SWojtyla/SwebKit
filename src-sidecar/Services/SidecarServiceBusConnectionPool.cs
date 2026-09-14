using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Sidecar implementation of <see cref="IServiceBusConnectionPool"/>. Every real (non-demo) Service Bus
/// endpoint request funnels through <see cref="GetOrCreate"/> instead of calling
/// <see cref="IServiceBusClientFactory"/> directly, so the same <see cref="IServiceBusClient"/> — and with it
/// one AMQP connection, one management-plane pipeline and one already-acquired credential — is reused across
/// requests for the same namespace. Built on the generic <see cref="ClientCache{TClient}"/> primitive,
/// matching <see cref="SidecarStorageConnectionPool"/>.
/// </summary>
/// <remarks>
/// This replaces per-request client construction across all sixteen handlers, none of which disposed what
/// they built. Opening a namespace fires <c>2 + topicCount</c> requests, so the old behaviour meant that many
/// simultaneous credential-chain resolutions and that many leaked AMQP connections.
/// <para>Demo clients are tagged <see cref="ConnectionOwnership.Borrowed"/> — <c>DemoModeService</c> hands out
/// long-lived singletons it disposes itself, so the cache must never dispose them.</para>
/// </remarks>
public sealed class SidecarServiceBusConnectionPool(IServiceBusClientFactory factory, DemoModeService demo)
    : IServiceBusConnectionPool, IAsyncDisposable
{
    private readonly ClientCache<IServiceBusClient> _cache = new();

    public IServiceBusClient GetOrCreate(ServiceBusNamespace ns)
    {
        ArgumentNullException.ThrowIfNull(ns);

        return _cache.GetOrAdd(ns.Id.ToString(), () => demo.IsDemoMode
            ? (demo.GetSbClient(ns), ConnectionOwnership.Borrowed)
            : (Create(ns), ConnectionOwnership.Factory))!;
    }

    public void Evict(string namespaceId) => _cache.Evict(namespaceId);

    public void InvalidateAll() => _cache.InvalidateAllSync();

    public ValueTask DisposeAsync() => _cache.DisposeAsync();

    private IServiceBusClient Create(ServiceBusNamespace ns) =>
        ns.AuthMode == SbAuthMode.ConnectionString
            ? factory.Create(ns.CredentialKey, ns.TransportType)
            : factory.CreateWithEntra(ns.FullyQualifiedNamespace, ns.TransportType);
}
