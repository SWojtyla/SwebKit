namespace SwebKit.Core.Abstractions;

/// <summary>
/// Shared connection pool for alert monitoring signal sources.
/// All connections are cached after first use and reused across polling intervals,
/// preventing per-poll TCP/AMQP/Redis connection churn in the background.
/// The pool owns the lifetime of every connection; signal sources must NOT dispose
/// clients obtained from this pool.
/// </summary>
public interface IMonitoringConnectionPool : IAsyncDisposable
{
    /// <summary>
    /// Returns the cached <see cref="IAksClient"/> for the current kubeconfig context,
    /// or <see langword="null"/> if AKS is not configured.
    /// Automatically recreates the client when the kubeconfig context or path changes.
    /// </summary>
    IAksClient? GetAksClient();

    /// <summary>
    /// Returns the cached <see cref="IAksClient"/> for an explicit <paramref name="context"/>.
    /// Falls back to <see cref="GetAksClient"/> when <paramref name="context"/> is null or empty.
    /// </summary>
    IAksClient? GetAksClient(string? context);

    /// <summary>
    /// Returns the cached <see cref="IServiceBusClient"/> for <paramref name="alias"/>,
    /// or <see langword="null"/> if the alias is not found or not credentialed.
    /// Connection is established lazily on first call and reused thereafter.
    /// </summary>
    IServiceBusClient? GetServiceBusClient(string alias);

    /// <summary>
    /// Returns the cached <see cref="IRedisClient"/> for <paramref name="displayName"/>,
    /// or <see langword="null"/> if the display name is not configured.
    /// Connection is established lazily on first call and reused thereafter.
    /// </summary>
    ValueTask<IRedisClient?> GetRedisClientAsync(string displayName, CancellationToken ct = default);

    /// <summary>
    /// Releases cached connections whose configuration has changed or whose resource no
    /// longer exists in the profile.  Call after the application profile is saved so that
    /// signal sources pick up updated credentials on the next poll.
    /// </summary>
    void InvalidateStaleConnections();

    /// <summary>
    /// Evicts the cached <see cref="IServiceBusClient"/> for <paramref name="alias"/> so that
    /// a fresh client is created on the next call to <see cref="GetServiceBusClient"/>.
    /// Use this when Entra credentials may have changed or a stale connection needs resetting.
    /// </summary>
    void EvictServiceBusClient(string alias);
}
