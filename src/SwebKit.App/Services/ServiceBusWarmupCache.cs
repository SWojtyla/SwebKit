using SwebKit.Core.Abstractions;

namespace SwebKit.App.Services;

/// <summary>
/// Cache of pre-warmed Service Bus clients. Clients are transferred to the caller on <see cref="TryGet(Guid)"/>;
/// callers own the returned client and are responsible for its disposal.
/// Unconsumed clients are disposed when the cache is invalidated or the service is disposed.
/// </summary>
public sealed class ServiceBusWarmupCache : IServiceBusWarmupCache
{
    private readonly Dictionary<Guid, IServiceBusClient> _clients = [];

    public void Store(Guid namespaceId, IServiceBusClient client)
    {
        var previous = _clients.TryGetValue(namespaceId, out var existing) ? existing : null;
        _clients[namespaceId] = client;
        DisposeClientIfPossible(previous);
    }

    /// <summary>
    /// Removes and returns the cached client for <paramref name="namespaceId"/>.
    /// Ownership transfers to the caller — the cache no longer holds a reference.
    /// Returns null when no warm client is available.
    /// </summary>
    public IServiceBusClient? TryGet(Guid namespaceId)
    {
        _clients.Remove(namespaceId, out var client);
        return client;
    }

    public void Invalidate()
    {
        // Dispose any clients that were never consumed by a page.
        var unconsumed = _clients.Values.ToList();
        _clients.Clear();
        foreach (var c in unconsumed)
        {
            DisposeClientIfPossible(c);
        }
    }

    private static void DisposeClientIfPossible(IServiceBusClient? client)
    {
        if (client is null) return;

        // Service Bus clients are async-disposable; fire-and-forget best-effort disposal.
        if (client is IAsyncDisposable asyncDisposable)
        {
            _ = asyncDisposable.DisposeAsync().AsTask().ContinueWith(
                static t => { _ = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
