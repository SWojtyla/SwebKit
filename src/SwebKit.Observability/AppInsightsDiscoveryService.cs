using Azure.ResourceManager;
using Azure.ResourceManager.ApplicationInsights;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Observability;

/// <summary>
/// Discovers Application Insights components across all accessible Azure subscriptions.
/// Uses DefaultAzureCredential. Results are cached for the lifetime of the service.
/// </summary>
public sealed class AppInsightsDiscoveryService : IObservabilityResourceDiscovery
{
    private List<ObservabilityResourceInfo>? _cache;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async IAsyncEnumerable<ObservabilityResourceInfo> DiscoverResourcesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache is not null)
            {
                foreach (var r in _cache)
                    yield return r;
                yield break;
            }

            _cache = [];
            // See AzureCredentialFactory for why EnvironmentCredential is excluded.
            var credential = AzureCredentialFactory.CreateDefault();
            var armClient = new ArmClient(credential);

            await foreach (var subscription in armClient.GetSubscriptions().GetAllAsync(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                var subId = subscription.Data.SubscriptionId;
                var subName = subscription.Data.DisplayName ?? subId;

                await foreach (var ai in subscription.GetApplicationInsightsComponentsAsync(cancellationToken: ct).ConfigureAwait(false))
                {
                    ct.ThrowIfCancellationRequested();

                    var info = new ObservabilityResourceInfo(
                        ResourceId: ai.Id!.ToString(),
                        Name: ai.Data.Name ?? ai.Id.Name,
                        SubscriptionId: subId,
                        SubscriptionName: subName,
                        ResourceGroup: ai.Id.ResourceGroupName ?? string.Empty,
                        Location: ai.Data.Location.ToString());

                    _cache.Add(info);
                    yield return info;
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Clears the in-memory cache so the next call re-scans Azure.</summary>
    public void InvalidateCache() => _cache = null;
}
