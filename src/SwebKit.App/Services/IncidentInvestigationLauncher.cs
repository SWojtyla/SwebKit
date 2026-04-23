using Microsoft.AspNetCore.Components;
using SwebKit.Core.Models;

namespace SwebKit.App.Services;

/// <summary>
/// App-layer singleton that carries one pending investigation seed from a source page
/// to IncidentTimelinePage. Source components call LaunchAsync; the destination page
/// calls TakePendingSeed on initialization to consume and clear the pending seed.
/// </summary>
public sealed class IncidentInvestigationLauncher
{
    private readonly NavigationManager _nav;
    private IncidentInvestigationSeed? _pendingSeed;

    public IncidentInvestigationLauncher(NavigationManager nav)
    {
        _nav = nav;
    }

    /// <summary>
    /// Stores the seed and navigates to /incident-timeline.
    /// The destination page picks up the seed via TakePendingSeed().
    /// </summary>
    public void Launch(IncidentInvestigationSeed seed)
    {
        _pendingSeed = seed;
        _nav.NavigateTo("/incident-timeline");
    }

    /// <summary>
    /// Returns and clears the pending seed. Returns null if no seed is waiting.
    /// Should be called once during IncidentTimelinePage initialization.
    /// </summary>
    public IncidentInvestigationSeed? TakePendingSeed()
    {
        var seed = _pendingSeed;
        _pendingSeed = null;
        return seed;
    }

    /// <summary>Whether a seed is currently waiting to be consumed.</summary>
    public bool HasPendingSeed => _pendingSeed is not null;
}
