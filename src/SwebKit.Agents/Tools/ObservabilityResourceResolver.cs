using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Resolves the optional <c>resource</c> argument shared by <c>query_logs</c> and
/// <c>get_metrics</c> to a concrete App Insights resource id: an ARM resource id passes
/// through verbatim, anything else is matched against
/// <see cref="IObservabilityResourceDiscovery"/> — exact name match first, then substring on
/// name or resource id. Ambiguity returns the candidate list so the model can retry with a
/// specific one instead of silently querying the wrong app's telemetry.
/// </summary>
internal static class ObservabilityResourceResolver
{
    /// <summary>Max candidates surfaced in an ambiguous-match error — enough for the model to
    /// pick, bounded so a broad substring can't flood the tool result.</summary>
    private const int MaxCandidates = 20;

    /// <returns><c>(resourceId, null)</c> on success, <c>(null, errorJson)</c> when the argument
    /// was given but couldn't be resolved to a single resource.</returns>
    public static async Task<(string? ResourceId, string? ErrorJson)> ResolveAsync(
        IObservabilityResourceDiscovery discovery, string? resource, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(resource))
            return (null, null);

        var trimmed = resource.Trim();
        if (trimmed.StartsWith('/'))
            return (trimmed, null);

        var matches = new List<ObservabilityResourceInfo>();
        await foreach (var r in discovery.DiscoverResourcesAsync(ct).ConfigureAwait(false))
        {
            if (r.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                || r.ResourceId.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                matches.Add(r);
        }

        // An exact name match beats substring hits: "contoso-api-dev" must not fail just
        // because "contoso-api" also matches staging and prod.
        var resolved = matches.Count == 1
            ? matches[0]
            : matches.FirstOrDefault(m => string.Equals(m.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (resolved is not null)
            return (resolved.ResourceId, null);

        return (null, matches.Count == 0
            ? JsonSerializer.Serialize(new
            {
                error = $"No Application Insights resource matches '{trimmed}'.",
                hint = "Run list_observability_resources to see what is discoverable, or pass a full ARM resource id.",
            })
            : JsonSerializer.Serialize(new
            {
                error = $"'{trimmed}' matches {matches.Count} observability resources — pass a more specific name or the full resource_id.",
                candidates = matches.Take(MaxCandidates).Select(m => new
                {
                    name = m.Name,
                    resource_id = m.ResourceId,
                    subscription = m.SubscriptionName,
                    resource_group = m.ResourceGroup,
                }),
            }));
    }
}
