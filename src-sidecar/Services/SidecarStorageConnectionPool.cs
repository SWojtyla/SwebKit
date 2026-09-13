using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Sidecar implementation of <see cref="IStorageConnectionPool"/>. Every real (non-demo) storage
/// endpoint request funnels through <see cref="GetOrCreate"/> instead of calling
/// <see cref="IStorageClientFactory"/> directly, so the same <see cref="IStorageClient"/> — and,
/// for AAD-backed accounts, the same already-acquired credential — is reused across requests for
/// the same account instead of rebuilt on every one. Built on the generic <see cref="ClientCache{TClient}"/>
/// primitive, which any other endpoint-level client cache (Service Bus/Redis/AKS) can reuse the same way.
/// </summary>
public sealed class SidecarStorageConnectionPool(IStorageClientFactory factory) : IStorageConnectionPool, IAsyncDisposable
{
    private readonly ClientCache<IStorageClient> _cache = new();

    public IStorageClient GetOrCreate(StorageConfig config) =>
        _cache.GetOrAdd(config.Id, () => (factory.Create(config), ConnectionOwnership.Factory))!;

    public void Evict(string accountId) => _cache.Evict(accountId);

    public void InvalidateAll() => _cache.InvalidateAllSync();

    public ValueTask DisposeAsync() => _cache.DisposeAsync();
}
