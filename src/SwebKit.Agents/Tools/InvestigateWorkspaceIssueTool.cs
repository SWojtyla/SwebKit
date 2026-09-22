using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.Agents.Tools.Sql;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Composite, cross-area tool (workspace-intelligence Module 3) — the workspace-scale analogue of
/// <see cref="InvestigatePodIssueTool"/>/<c>AnalyzeQueueHealthTool</c>/<c>AnalyzeCacheHealthTool</c>.
/// Starts from one resource in the user-curated workspace maps (Settings' Map tab), walks up to
/// <see cref="MaxHops"/> hops of declared relationships, and re-invokes each related resource's own
/// single-area investigation tool via <see cref="IAgentToolRegistry"/> (by name — the same dispatch
/// mechanism the model's own tool calls already go through), merging everything into one report.
///
/// Map membership is enrichment, not a gate: when no node in any map matches the hint, the tool
/// investigates the hinted resource directly instead of erroring — an alert (or a user) asking
/// about an unmapped resource should still get real data back.
///
/// Tagged <see cref="Tools.FeatureArea.Workspace"/>, which — unlike <see cref="Tools.FeatureArea.Observability"/>
/// — is NOT exempt from <c>SidecarAgentChatService.ResolveTools</c>'s per-area filter. It only
/// becomes visible in a contextual panel once that turn requests <c>scope: "workspace"</c> (the
/// "search across my whole workspace" escalation); the global <c>/agent</c> page has no area filter
/// at all, so it's always visible there, same as every other tool.
/// </summary>
public sealed class InvestigateWorkspaceIssueTool : IAgentTool
{
    private const int MaxHops = 2;

    private readonly ProfileRepository _profiles;
    private readonly IServiceProvider _services;
    private readonly IMonitoringConnectionPool _connectionPool;
    private readonly AppStateService _appState;

    /// <summary>
    /// Takes <see cref="IServiceProvider"/> rather than <see cref="IAgentToolRegistry"/> directly to
    /// break an otherwise-genuine circular dependency: <c>AgentToolRegistry</c> is constructed from
    /// <c>IEnumerable&lt;IAgentTool&gt;</c>, and this tool is one of those <c>IAgentTool</c>s — asking
    /// the DI container for the registry up front, in this class's own constructor, would fail at
    /// startup. Resolving it lazily in <see cref="ExecuteAsync"/> instead works because by the time
    /// any tool call actually runs, the whole container (registry included) has already finished
    /// building.
    /// </summary>
    public InvestigateWorkspaceIssueTool(ProfileRepository profiles, IServiceProvider services, IMonitoringConnectionPool connectionPool, AppStateService appState)
    {
        _profiles = profiles;
        _services = services;
        _connectionPool = connectionPool;
        _appState = appState;
    }

    public string Name => "investigate_workspace_issue";

    public string Description =>
        "Investigates an issue across the workspace, starting from one resource and following up to " +
        "2 hops of relationships the user has declared on the workspace Map (Settings), running each " +
        "related resource's own investigation/health tool and merging the results into one report. " +
        "The declared map is already in your context — use this tool to actually inspect the related " +
        "resources, not to discover the relationships. Works even when the resource is not on any " +
        "map — it then investigates the named resource directly, without relationship correlation.";

    public FeatureArea FeatureArea => FeatureArea.Workspace;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "area": { "type": "string", "enum": ["Aks", "ServiceBus", "Redis", "Storage", "Sql"], "description": "Which area the starting resource belongs to." },
            "resource_hint": { "type": "string", "description": "A word or phrase identifying the starting resource, e.g. a deployment name, queue name, or cache display name." },
            "context": { "type": "string", "description": "Optional kubeconfig context for AKS resources — pin the lookup and the cluster calls to this context instead of the globally configured one." },
            "map_id": { "type": "string", "description": "Optional: restrict the starting-resource lookup to a single workspace map by id. Omit to search every map." }
          },
          "required": ["area", "resource_hint"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var areaStr = arguments.GetProperty("area").GetString() ?? string.Empty;
        var hint = arguments.GetProperty("resource_hint").GetString() ?? string.Empty;
        var context = arguments.TryGetProperty("context", out var ctxEl) && ctxEl.ValueKind == JsonValueKind.String
            ? ctxEl.GetString()
            : null;
        var mapId = arguments.TryGetProperty("map_id", out var mapEl) && mapEl.ValueKind == JsonValueKind.String
            ? mapEl.GetString()
            : null;

        if (!Enum.TryParse<WorkspaceResourceArea>(areaStr, ignoreCase: true, out var area))
            return JsonSerializer.Serialize(new { error = $"Unknown area '{areaStr}'." });

        var maps = _profiles.Config.EffectiveMaps();
        if (!string.IsNullOrWhiteSpace(mapId))
            maps = maps.Where(m => m.Id == mapId);

        var match = WorkspaceMapLookup.FindNode(maps, area, hint, context);
        if (match is null)
        {
            // Not on any map — investigate the hinted resource directly rather than refusing:
            // a resource doesn't need to be curated before it can be inspected.
            var direct = new WorkspaceResourceNode
            {
                Area = area,
                ResourceKey = hint,
                DisplayLabel = hint,
                KubeconfigContext = context,
            };
            var directReport = await InvestigateNodeAsync(direct, ct);
            return JsonSerializer.Serialize(new
            {
                starting_resource = new { area = area.ToString(), resource_key = hint, display_label = hint },
                related_resources_investigated = 0,
                reports = new[] { directReport },
                note = $"'{hint}' is not on any workspace Map — investigated directly; no declared relationships to correlate. Add it in Settings → Map to enable relationship walks.",
            });
        }

        var (map, startNode) = match.Value;
        var relatedNodes = WalkRelationships(map, startNode.Id, MaxHops);
        var reports = new List<object>();
        foreach (var node in relatedNodes)
            reports.Add(await InvestigateNodeAsync(node, ct));

        return JsonSerializer.Serialize(new
        {
            starting_resource = new { area = startNode.Area.ToString(), startNode.ResourceKey, startNode.DisplayLabel },
            map = map.Name,
            related_resources_investigated = reports.Count,
            reports,
            note = relatedNodes.Count == 0
                ? "No relationships are declared for this resource on the workspace Map yet — nothing to correlate."
                : "Relationships come from the user-curated workspace Map (Settings), not automatic inference.",
        });
    }

    /// <summary>Breadth-first walk, bounded to <paramref name="maxHops"/> so a densely-connected
    /// topology can't fan out unboundedly.</summary>
    private static List<WorkspaceResourceNode> WalkRelationships(WorkspaceTopology topology, string startNodeId, int maxHops)
    {
        var visited = new HashSet<string> { startNodeId };
        var frontier = new List<string> { startNodeId };
        var result = new List<WorkspaceResourceNode>();

        for (var hop = 0; hop < maxHops && frontier.Count > 0; hop++)
        {
            var next = new List<string>();
            foreach (var nodeId in frontier)
            {
                var neighborIds = topology.Relationships
                    .Where(r => r.FromNodeId == nodeId || r.ToNodeId == nodeId)
                    .Select(r => r.FromNodeId == nodeId ? r.ToNodeId : r.FromNodeId);

                foreach (var neighborId in neighborIds)
                {
                    if (!visited.Add(neighborId))
                        continue;

                    next.Add(neighborId);
                    var node = topology.Nodes.FirstOrDefault(n => n.Id == neighborId);
                    if (node is not null)
                        result.Add(node);
                }
            }
            frontier = next;
        }

        return result;
    }

    private async Task<object> InvestigateNodeAsync(WorkspaceResourceNode node, CancellationToken ct)
    {
        try
        {
            var registry = _services.GetRequiredService<IAgentToolRegistry>();

            switch (node.Area)
            {
                case WorkspaceResourceArea.Aks:
                    return await InvestigateAksNodeAsync(node, registry, ct);

                case WorkspaceResourceArea.ServiceBus:
                {
                    var queueName = node.ResourceKey.Contains('/') ? node.ResourceKey.Split('/')[^1] : node.ResourceKey;
                    var raw = await registry.ExecuteAsync("analyze_queue_health", BuildArgs(new { queue_name = queueName }), ct);
                    return new { area = node.Area.ToString(), node.DisplayLabel, result = JsonDocument.Parse(raw).RootElement };
                }

                case WorkspaceResourceArea.Redis:
                {
                    var raw = await registry.ExecuteAsync("analyze_cache_health", BuildArgs(new { cache_id = node.ResourceKey }), ct);
                    return new { area = node.Area.ToString(), node.DisplayLabel, result = JsonDocument.Parse(raw).RootElement };
                }

                case WorkspaceResourceArea.Storage:
                {
                    // Nodes key on the account name, optionally with a container appended after
                    // '/' (see WorkspaceResourceNode.ResourceKey) — analyze_storage_health takes
                    // just the account part.
                    var accountKey = node.ResourceKey.Split('/')[0];
                    var raw = await registry.ExecuteAsync("analyze_storage_health", BuildArgs(new { account = accountKey }), ct);
                    return new { area = node.Area.ToString(), node.DisplayLabel, result = JsonDocument.Parse(raw).RootElement };
                }

                case WorkspaceResourceArea.Sql:
                {
                    // Nodes key on "server" or "server/database" (see BuildSqlCandidates) — map the
                    // server part back to the configured connection so check_sql_health can run.
                    var keyParts = node.ResourceKey.Split('/', 2);
                    var connection = SqlToolContext.GetConnections(_appState, _profiles)
                        .FirstOrDefault(c => c.Server.Equals(keyParts[0], StringComparison.OrdinalIgnoreCase));
                    if (connection is null)
                        return new { area = node.Area.ToString(), node.DisplayLabel, skipped = $"No configured SQL connection matches '{keyParts[0]}'." };

                    var raw = await registry.ExecuteAsync("check_sql_health", BuildArgs(new
                    {
                        connection_id = connection.Id,
                        database = keyParts.Length > 1 ? keyParts[1] : null,
                    }), ct);
                    return new { area = node.Area.ToString(), node.DisplayLabel, result = JsonDocument.Parse(raw).RootElement };
                }

                default:
                    return new { area = node.Area.ToString(), node.DisplayLabel, skipped = $"No composite investigation tool exists for {node.Area} yet." };
            }
        }
        catch (Exception ex)
        {
            return new { area = node.Area.ToString(), node.DisplayLabel, error = ex.Message };
        }
    }

    private async Task<object> InvestigateAksNodeAsync(WorkspaceResourceNode node, IAgentToolRegistry registry, CancellationToken ct)
    {
        var parts = node.ResourceKey.Split('/', 2);
        var client = _connectionPool.GetAksClient(node.KubeconfigContext);
        if (client is null)
            return new { area = node.Area.ToString(), node.DisplayLabel, skipped = "AKS is not configured." };

        var ns = parts[0];
        if (parts.Length != 2)
        {
            // Namespace-level key ("ns" with no deployment) — the map's AKS picker produces these.
            // Inspect the namespace as a whole: every pod's readiness, then a full investigation of
            // the least healthy one so the report still contains real diagnostic depth.
            var pods = await client.GetPodsAsync(ns, ct: ct);
            if (pods.Count == 0)
                return new { area = node.Area.ToString(), node.DisplayLabel, skipped = $"No pods found in namespace '{ns}'." };

            var suspect = pods
                .Where(p => !p.Ready || p.RestartCount > 0)
                .OrderByDescending(p => p.RestartCount)
                .FirstOrDefault();
            if (suspect is null)
                return new
                {
                    area = node.Area.ToString(),
                    node.DisplayLabel,
                    result = new { namespace_name = ns, pod_count = pods.Count, summary = $"All {pods.Count} pod(s) in '{ns}' report ready." },
                };

            var suspectRaw = await registry.ExecuteAsync(
                "investigate_pod_issue",
                BuildArgs(new { @namespace = ns, pod_name = suspect.Name, context = node.KubeconfigContext }), ct);
            return new
            {
                area = node.Area.ToString(),
                node.DisplayLabel,
                result = JsonDocument.Parse(suspectRaw).RootElement,
                note = $"Namespace-level node — investigated '{suspect.Name}', the least healthy of {pods.Count} pod(s) in '{ns}'.",
            };
        }

        var deployment = parts[1];
        var deployPods = await client.GetPodsAsync(ns, ct: ct);
        var pod = deployPods.FirstOrDefault(p => p.Name.StartsWith(deployment + "-", StringComparison.OrdinalIgnoreCase));
        if (pod is null)
            return new { area = node.Area.ToString(), node.DisplayLabel, skipped = $"No running pod found for deployment '{deployment}' in namespace '{ns}'." };

        var raw = await registry.ExecuteAsync(
            "investigate_pod_issue",
            BuildArgs(new { @namespace = ns, pod_name = pod.Name, context = node.KubeconfigContext }), ct);
        return new { area = node.Area.ToString(), node.DisplayLabel, result = JsonDocument.Parse(raw).RootElement };
    }

    private static JsonElement BuildArgs(object obj) => JsonSerializer.SerializeToDocument(obj).RootElement;
}
