using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Sidecar implementation of <see cref="ISqlConnectionPool"/>. Every real (non-demo) SQL endpoint
/// request funnels through <see cref="GetOrCreateAsync"/> so the same <see cref="ISqlClient"/> —
/// and the Entra credential inside it — is reused across requests for the same connection.
/// Built on the generic <see cref="ClientCache{TClient}"/> primitive, matching
/// <see cref="SidecarRedisConnectionPool"/>.
/// </summary>
/// <remarks>
/// Demo-mode requests bypass the cache entirely: <c>DemoModeService</c> hands out long-lived
/// singletons it disposes itself, and letting them share cache keys with real connections let a
/// factory-built client poison demo mode (same reasoning as the Redis pool).
/// </remarks>
public sealed class SidecarSqlConnectionPool(ISqlClientFactory factory, DemoModeService demo)
    : ISqlConnectionPool, IAsyncDisposable
{
    private readonly ClientCache<ISqlClient> _cache = new();

    public async ValueTask<ISqlClient> GetOrCreateAsync(SqlConnectionEntry connection, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (demo.IsDemoMode)
            return demo.GetSqlClient(connection);

        return (await _cache.GetOrAddAsync(
            connection.Id,
            async token =>
                (await factory.CreateAsync(connection, token).ConfigureAwait(false), ConnectionOwnership.Factory),
            ct).ConfigureAwait(false))!;
    }

    public void Evict(string connectionId) => _cache.Evict(connectionId);

    public void InvalidateAll() => _cache.InvalidateAllSync();

    public ValueTask DisposeAsync() => _cache.DisposeAsync();
}
