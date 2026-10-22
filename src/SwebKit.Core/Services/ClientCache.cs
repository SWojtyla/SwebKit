using System.Collections.Concurrent;

namespace SwebKit.Core.Services;

/// <summary>
/// Tags a <see cref="ClientCache{TClient}"/> entry with who is responsible for disposing it.
/// </summary>
public enum ConnectionOwnership
{
    /// <summary>Created by the cache's factory delegate; the cache owns disposal.</summary>
    Factory,

    /// <summary>Provided externally (e.g. a demo-mode singleton owned by another service); the cache never disposes it.</summary>
    Borrowed,
}

/// <summary>
/// Thread-safe cache of long-lived SDK clients keyed by a string identity (a storage account id,
/// a Service Bus namespace alias, a kubeconfig context, ...). Building a client is often not free —
/// for AAD-backed clients it also means acquiring a fresh <c>DefaultAzureCredential</c> token
/// chain — so keying the cache lets the same client (and credential) be reused across many
/// requests instead of rebuilt on every one. See docs/pitfalls/azure-sdk.md.
///
/// <para>Generic on purpose: any endpoint- or service-level client cache (Storage today; Service
/// Bus/Redis/AKS request endpoints could adopt the same pattern later) gets identical,
/// already-tested caching and disposal behavior for free — one instance of this type per
/// <typeparamref name="TClient"/> instead of a bespoke <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// and matching dispose helpers per caller.</para>
///
/// <para><b>Ownership:</b> every entry carries a <see cref="ConnectionOwnership"/> tag.
/// <see cref="ConnectionOwnership.Factory"/> entries are owned by this cache and disposed on
/// eviction/invalidation/disposal. <see cref="ConnectionOwnership.Borrowed"/> entries (e.g. a
/// demo-mode singleton owned by another service) are cached for lookup but never disposed here.</para>
///
/// <para><b>Single flight:</b> entries live in a <see cref="SingleFlightCache{T}"/>, so concurrent
/// cold misses for one key share a single factory call instead of each building a client and the
/// losers disposing theirs — a duplicate build is a duplicate credential-chain walk or connection
/// handshake. A factory that throws leaves nothing cached; the next call retries. An entry evicted
/// while still being built is disposed as soon as its build completes.</para>
/// </summary>
public sealed class ClientCache<TClient> : IAsyncDisposable
    where TClient : class
{
    private readonly record struct Entry(TClient Client, ConnectionOwnership Ownership);

    private readonly SingleFlightCache<Entry> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Set by <see cref="DisposeAsync"/>; once true, acquisitions return null and invalidation is a no-op.</summary>
    private volatile bool _closed;

    /// <summary>
    /// Returns the cached client for <paramref name="key"/>, or builds and caches one via
    /// <paramref name="factory"/> if absent. Returns <see langword="null"/> once the cache has
    /// been disposed.
    /// </summary>
    public TClient? GetOrAdd(string key, Func<(TClient Client, ConnectionOwnership Ownership)> factory)
    {
        if (_closed)
            return null;

        return _cache.GetOrCreate(key, () =>
        {
            var (client, ownership) = factory();
            return new Entry(client, ownership);
        }).Client;
    }

    /// <summary>
    /// Async variant of <see cref="GetOrAdd"/> for factories that must await (e.g. Redis). The build
    /// is shared by every concurrent caller for <paramref name="key"/>, so the factory receives
    /// <see cref="CancellationToken.None"/>; <paramref name="ct"/> only abandons this caller's wait.
    /// </summary>
    public async ValueTask<TClient?> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<(TClient Client, ConnectionOwnership Ownership)>> factory,
        CancellationToken ct = default)
    {
        if (_closed)
            return null;

        var entry = await _cache.GetOrCreateAsync(key, async () =>
        {
            var (client, ownership) = await factory(CancellationToken.None).ConfigureAwait(false);
            return new Entry(client, ownership);
        }, ct).ConfigureAwait(false);
        return entry.Client;
    }

    /// <summary>Removes and disposes (if factory-owned) the entry for <paramref name="key"/>, if any.</summary>
    public void Evict(string key)
    {
        if (_closed)
            return;

        if (_cache.TryRemove(key, out var build))
            SingleFlightCache.WhenBuilt(build, DisposeOwnedSync);
    }

    /// <summary>Synchronously evicts and disposes every factory-owned entry; the cache stays usable afterwards.</summary>
    public void InvalidateAllSync()
    {
        if (_closed)
            return;

        foreach (var (_, build) in _cache.RemoveAll())
            SingleFlightCache.WhenBuilt(build, DisposeOwnedSync);
    }

    /// <summary>Async variant of <see cref="InvalidateAllSync"/>.</summary>
    public async ValueTask InvalidateAllAsync(CancellationToken ct = default)
    {
        if (_closed)
            return;

        foreach (var (_, build) in _cache.RemoveAll())
        {
            ct.ThrowIfCancellationRequested();
            await DisposeRemovedAsync(build).ConfigureAwait(false);
        }
    }

    /// <summary>Permanently closes the cache, disposing every factory-owned entry. Idempotent.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_closed)
            return;
        _closed = true;

        foreach (var (_, build) in _cache.RemoveAll())
        {
            try
            {
                await DisposeRemovedAsync(build).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort — a failing disposal must not break shutdown or other entries' disposal.
            }
        }
    }

    /// <summary>Disposes a removed entry now if it is built, or once its in-flight build completes.</summary>
    private static async ValueTask DisposeRemovedAsync(Task<Entry> build)
    {
        if (!build.IsCompletedSuccessfully)
        {
            SingleFlightCache.WhenBuilt(build, DisposeOwnedSync);
            return;
        }

        var entry = build.Result;
        if (entry.Ownership == ConnectionOwnership.Factory)
            await DisposeClientAsync(entry.Client).ConfigureAwait(false);
    }

    private static void DisposeOwnedSync(Entry entry)
    {
        if (entry.Ownership == ConnectionOwnership.Factory)
            DisposeSync(entry.Client);
    }

    private static void DisposeSync(TClient client)
    {
        try
        {
            if (client is IAsyncDisposable ad)
                ad.DisposeAsync().AsTask().GetAwaiter().GetResult();
            else if (client is IDisposable d)
                d.Dispose();
        }
        catch
        {
            // Best-effort — a failing disposal must not break the caller's control flow.
        }
    }

    private static async ValueTask DisposeClientAsync(TClient client)
    {
        if (client is IAsyncDisposable ad)
            await ad.DisposeAsync().ConfigureAwait(false);
        else if (client is IDisposable d)
            d.Dispose();
    }
}
