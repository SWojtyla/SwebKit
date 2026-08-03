using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Composite, cross-area tool (workspace-intelligence Module 3) — the workspace-scale analogue of
/// <see cref="InvestigatePodIssueTool"/>/<c>AnalyzeQueueHealthTool</c>/<c>AnalyzeCacheHealthTool</c>.
/// Starts from one resource in the user-curated workspace topology (Settings' Map tab), walks up to
/// <see cref="MaxHops"/> hops of declared relationships, and re-invokes each related resource's own
/// single-area investigation tool via <see cref="IAgentToolRegistry"/> (by name — the same dispatch
/// mechanism the model's own tool calls already go through), merging everything into one report.
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

    /// <summary>
    /// Takes <see cref="IServiceProvider"/> rather than <see cref="IAgentToolRegistry"/> directly to
    /// break an otherwise-genuine circular dependency: <c>AgentToolRegistry</c> is constructed from
    /// <c>IEnumerable&lt;IAgentTool&gt;</c>, and this tool is one of those <c>IAgentTool</c>s — asking
    /// the DI container for the registry up front, in this class's own constructor, would fail at
    /// startup. Resolving it lazily in <see cref="ExecuteAsync"/> instead works because by the time
    /// any tool call actually runs, the whole container (registry included) has already finished
    /// building.
    /// </summary>
    public InvestigateWorkspaceIssueTool(ProfileRepository profiles, IServiceProvider services, IMonitoringConnectionPool connectionPool)
    {
        _profiles = profiles;
        _services = services;
        _connectionPool = connectionPool;
    }

    public string Name => "investigate_workspace_issue";

    public string Description =>
        "Investigates an issue across the workspace, starting from one resource and following up to " +
        "2 hops of relationships the user has declared on the workspace Map (Settings), running each " +
        "related resource's own investigation/health tool and merging the results into one report. " +
        "Only useful if relationships have been declared — returns a note, not an error, if none exist yet.";

    public FeatureArea FeatureArea => FeatureArea.Workspace;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "area": { "type": "string", "enum": ["Aks", "ServiceBus", "Redis", "Storage"], "description": "Which area the starting resource belongs to." },
            "resource_hint": { "type": "string", "description": "A word or phrase identifying the starting resource, e.g. a deployment name, queue name, or cache display name." }
          },
          "required": ["area", "resource_hint"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var areaStr = arguments.GetProperty("area").GetString() ?? string.Empty;
        var hint = arguments.GetProperty("resource_hint").GetString() ?? string.Empty;

        if (!Enum.TryParse<WorkspaceResourceArea>(areaStr, ignoreCase: true, out var area))
            return JsonSerializer.Serialize(new { error = $"Unknown area '{areaStr}'." });

        var topology = _profiles.Config.Topology;
        var startNode = topology.Nodes.FirstOrDefault(n =>
            n.Area == area &&
            (n.ResourceKey.Contains(hint, StringComparison.OrdinalIgnoreCase) ||
             n.DisplayLabel.Contains(hint, StringComparison.OrdinalIgnoreCase)));

        if (startNode is null)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"No workspace topology node found for area '{areaStr}' matching '{hint}'. Add it on the Map settings tab first.",
            });
        }

        var relatedNodes = WalkRelationships(topology, startNode.Id, MaxHops);
        var reports = new List<object>();
        foreach (var node in relatedNodes)
            reports.Add(await InvestigateNodeAsync(node, ct));

        return JsonSerializer.Serialize(new
        {
            starting_resource = new { area = startNode.Area.ToString(), startNode.ResourceKey, startNode.DisplayLabel },
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

                default:
                    // Storage has no composite investigation/health tool yet (ai-augmented-app Module
                    // 4 only added Propose*/Get*/List* tools for it) — an honest gap, not a crash.
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
        if (parts.Length != 2)
            return new { area = node.Area.ToString(), node.DisplayLabel, skipped = "AKS resource key isn't in 'namespace/deployment' shape." };

        var (ns, deployment) = (parts[0], parts[1]);
        var client = _connectionPool.GetAksClient();
        if (client is null)
            return new { area = node.Area.ToString(), node.DisplayLabel, skipped = "AKS is not configured." };

        var pods = await client.GetPodsAsync(ns, ct: ct);
        var pod = pods.FirstOrDefault(p => p.Name.StartsWith(deployment + "-", StringComparison.OrdinalIgnoreCase));
        if (pod is null)
            return new { area = node.Area.ToString(), node.DisplayLabel, skipped = $"No running pod found for deployment '{deployment}' in namespace '{ns}'." };

        var raw = await registry.ExecuteAsync("investigate_pod_issue", BuildArgs(new { @namespace = ns, pod_name = pod.Name }), ct);
        return new { area = node.Area.ToString(), node.DisplayLabel, result = JsonDocument.Parse(raw).RootElement };
    }

    private static JsonElement BuildArgs(object obj) => JsonSerializer.SerializeToDocument(obj).RootElement;
}
