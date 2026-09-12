using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Sidecar.Endpoints;

public static class ObservabilityEndpoints
{
    public static void MapObservabilityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/observability/resources", GetResourcesAsync);
    }

    internal static async Task<IResult> GetResourcesAsync(
        IObservabilityResourceDiscovery discovery,
        bool refresh = false,
        CancellationToken ct = default)
    {
        if (refresh)
        {
            discovery.InvalidateCache();
        }

        // Azure's AuthenticationFailedException used to be caught here and turned into a bodyless
        // 401. That mapping belongs in the global exception handler (Program.cs) alongside the other
        // auth exceptions: it applies to every Azure-backed endpoint, it logs, and it returns the
        // standard {error} body with an actionable message instead of an empty response.
        var resources = new List<ObservabilityResourceInfo>();
        await foreach (var resource in discovery.DiscoverResourcesAsync(ct).ConfigureAwait(false))
        {
            resources.Add(resource);
        }

        return Results.Ok(resources);
    }
}
