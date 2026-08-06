using Azure.Identity;
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

        try
        {
            var resources = new List<ObservabilityResourceInfo>();
            await foreach (var resource in discovery.DiscoverResourcesAsync(ct).ConfigureAwait(false))
            {
                resources.Add(resource);
            }

            return Results.Ok(resources);
        }
        catch (AuthenticationFailedException)
        {
            return Results.Unauthorized();
        }
    }
}
