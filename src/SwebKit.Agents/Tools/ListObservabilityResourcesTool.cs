using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Lists the App Insights resources discoverable through <see cref="IObservabilityResourceDiscovery"/>.
/// This is the agent's answer to "the app I'm being asked about isn't the configured
/// observability resource" — the model enumerates, then passes a name or resource_id as the
/// <c>resource</c> argument of <c>query_logs</c>/<c>get_metrics</c>.
/// </summary>
public sealed class ListObservabilityResourcesTool : IAgentTool
{
    /// <summary>Discovery can yield hundreds of components across subscriptions; the list is
    /// bounded and <c>truncated</c> flags when it clipped.</summary>
    private const int MaxResources = 100;

    private readonly IObservabilityResourceDiscovery _discovery;
    private readonly AppStateService _appState;

    public ListObservabilityResourcesTool(IObservabilityResourceDiscovery discovery, AppStateService appState)
    {
        _discovery = discovery;
        _appState = appState;
    }

    public string Name => "list_observability_resources";

    public string Description =>
        "Lists the Application Insights resources discoverable across the configured Azure " +
        "subscriptions. Call this when the app in question isn't the configured observability " +
        "resource or query_logs reported 'not configured' — then pass the result's name or " +
        "resource_id as the 'resource' argument of query_logs/get_metrics.";

    public FeatureArea FeatureArea => FeatureArea.Observability;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "filter": {
              "type": "string",
              "description": "Optional case-insensitive substring filter on resource name or ARM id (e.g. 'sign', 'prd')"
            }
          },
          "required": []
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var filter = arguments.TryGetProperty("filter", out var f) && f.ValueKind == JsonValueKind.String
            ? f.GetString()
            : null;

        var resources = new List<object>();
        var total = 0;
        try
        {
            await foreach (var r in _discovery.DiscoverResourcesAsync(ct).ConfigureAwait(false))
            {
                if (filter is { Length: > 0 }
                    && !r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !r.ResourceId.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                total++;
                if (resources.Count < MaxResources)
                {
                    resources.Add(new
                    {
                        name = r.Name,
                        resource_id = r.ResourceId,
                        subscription = r.SubscriptionName,
                        resource_group = r.ResourceGroup,
                        location = r.Location,
                    });
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Resource discovery failed: {ex.Message}" });
        }

        return JsonSerializer.Serialize(new
        {
            configured_resource_id = _appState.Config.ObservabilityConfig?.SelectedResourceId,
            count = total,
            truncated = total > resources.Count,
            resources,
        });
    }
}
