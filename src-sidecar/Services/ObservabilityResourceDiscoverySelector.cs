using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Observability;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Routes observability resource discovery to the real Azure App Insights scanner or the fixed
/// demo discovery set based on the current demo-mode flag.
/// </summary>
public sealed class ObservabilityResourceDiscoverySelector : IObservabilityResourceDiscovery
{
    private readonly AppStateService _appState;
    private readonly AppInsightsDiscoveryService _real;
    private readonly DemoObservabilityResourceDiscovery _demo = new();

    public ObservabilityResourceDiscoverySelector(AppStateService appState, AppInsightsDiscoveryService real)
    {
        _appState = appState;
        _real = real;
    }

    public IAsyncEnumerable<ObservabilityResourceInfo> DiscoverResourcesAsync(CancellationToken ct = default)
    {
        return _appState.UseDemoData ? _demo.DiscoverResourcesAsync(ct) : _real.DiscoverResourcesAsync(ct);
    }

    public void InvalidateCache()
    {
        _real.InvalidateCache();
    }
}
