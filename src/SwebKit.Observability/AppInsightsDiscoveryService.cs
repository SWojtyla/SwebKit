using Azure.ResourceManager;
using Azure.ResourceManager.ApplicationInsights;
using Azure.ResourceManager.Resources;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Observability;

/// <summary>
/// Discovers Application Insights components across all accessible Azure subscriptions.
/// Uses DefaultAzureCredential. Results are cached for the lifetime of the service.
/// </summary>
public sealed class AppInsightsDiscoveryService : IObservabilityResourceDiscovery, IDisposable
{
    /// <summary>Per-subscription ARM listings run concurrently (results still stream in subscription
    /// order) — bounded so a tenant with dozens of subscriptions doesn't trip ARM throttling.</summary>
    private const int MaxConcurrentSubscriptions = 8;

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

            var subscriptions = new List<SubscriptionResource>();
            await foreach (var subscription in armClient.GetSubscriptions().GetAllAsync(ct).ConfigureAwait(false))
                subscriptions.Add(subscription);

            await foreach (var info in OrderedFanOut.StreamInOrderAsync<SubscriptionResource, ObservabilityResourceInfo>(
                subscriptions, ListComponentsAsync, MaxConcurrentSubscriptions, ct).ConfigureAwait(false))
            {
                _cache.Add(info);
                yield return info;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<IReadOnlyList<ObservabilityResourceInfo>> ListComponentsAsync(
        SubscriptionResource subscription,
        CancellationToken ct)
    {
        var subId = subscription.Data.SubscriptionId;
        var subName = subscription.Data.DisplayName ?? subId;
        var components = new List<ObservabilityResourceInfo>();

        await foreach (var ai in subscription.GetApplicationInsightsComponentsAsync(cancellationToken: ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            components.Add(new ObservabilityResourceInfo(
                ResourceId: ai.Id!.ToString(),
                Name: ai.Data.Name ?? ai.Id.Name,
                SubscriptionId: subId,
                SubscriptionName: subName,
                ResourceGroup: ai.Id.ResourceGroupName ?? string.Empty,
                Location: ai.Data.Location.ToString()));
        }

        return components;
    }

    /// <summary>Clears the in-memory cache so the next call re-scans Azure.</summary>
    public void InvalidateCache() => _cache = null;

    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
