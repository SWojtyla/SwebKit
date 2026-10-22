using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Sql;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Sql;

/// <summary>
/// Discovers SQL servers and their databases across all accessible Azure subscriptions via ARM,
/// using the shared <see cref="AzureCredentialFactory"/> credential — the SQL analogue of
/// <see cref="SwebKit.Observability.AppInsightsDiscoveryService"/>. Results are cached for the
/// lifetime of the service.
/// </summary>
public sealed class SqlServerDiscoveryService : ISqlResourceDiscovery, IDisposable
{
    /// <summary>Per-subscription ARM listings run concurrently (results still stream in subscription
    /// order) — bounded so a tenant with dozens of subscriptions doesn't trip ARM throttling.</summary>
    private const int MaxConcurrentSubscriptions = 8;

    private List<SqlDiscoveredServer>? _cache;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async IAsyncEnumerable<SqlDiscoveredServer> DiscoverServersAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache is not null)
            {
                foreach (var s in _cache)
                    yield return s;
                yield break;
            }

            _cache = [];
            // See AzureCredentialFactory for why EnvironmentCredential is excluded.
            var credential = AzureCredentialFactory.CreateDefault();
            var armClient = new ArmClient(credential);

            var subscriptions = new List<SubscriptionResource>();
            await foreach (var subscription in armClient.GetSubscriptions().GetAllAsync(ct).ConfigureAwait(false))
                subscriptions.Add(subscription);

            await foreach (var discovered in OrderedFanOut.StreamInOrderAsync<SubscriptionResource, SqlDiscoveredServer>(
                subscriptions, ListServersAsync, MaxConcurrentSubscriptions, ct).ConfigureAwait(false))
            {
                _cache.Add(discovered);
                yield return discovered;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<IReadOnlyList<SqlDiscoveredServer>> ListServersAsync(
        SubscriptionResource subscription,
        CancellationToken ct)
    {
        var subId = subscription.Data.SubscriptionId;
        var subName = subscription.Data.DisplayName ?? subId;
        var servers = new List<SqlDiscoveredServer>();

        await foreach (var server in subscription.GetSqlServersAsync(cancellationToken: ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            var discovered = new SqlDiscoveredServer
            {
                ServerFqdn = server.Data.FullyQualifiedDomainName ?? server.Data.Name ?? server.Id.Name,
                Name = server.Data.Name ?? server.Id.Name,
                ResourceGroup = server.Id.ResourceGroupName ?? string.Empty,
                SubscriptionId = subId,
                ResourceId = server.Id.ToString(),
                SubscriptionName = subName,
                Location = server.Data.Location.ToString(),
            };

            try
            {
                await foreach (var db in server.GetSqlDatabases().GetAllAsync(cancellationToken: ct).ConfigureAwait(false))
                {
                    ct.ThrowIfCancellationRequested();
                    if (db.Data.Name is { Length: > 0 } dbName && dbName != "master")
                        discovered.Databases.Add(dbName);
                }
            }
            catch (Exception) when (ct is { IsCancellationRequested: false })
            {
                // Database listing is best-effort — a server the caller can see but not
                // enumerate still shows up as a connection candidate.
            }

            servers.Add(discovered);
        }

        return servers;
    }

    /// <summary>Clears the in-memory cache so the next call re-scans Azure.</summary>
    public void InvalidateCache() => _cache = null;

    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
