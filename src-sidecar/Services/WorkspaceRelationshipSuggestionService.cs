using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Sidecar.Services;

/// <summary>A candidate relationship the heuristic scan found but the user hasn't confirmed —
/// workspace-intelligence Module 2. Never persisted; recomputed on demand by
/// <see cref="WorkspaceRelationshipSuggestionService.GetSuggestionsAsync"/> each time the Map view
/// asks for it, so accepting or dismissing one is just "don't show this pair again this session" on
/// the frontend, not a server-side state transition.</summary>
public sealed class WorkspaceRelationshipSuggestion
{
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }

    /// <summary>Human-readable explanation of what matched (e.g. "Pod config in prod/api contains a
    /// value matching \"orders-queue\""), shown directly in the UI per the plan's requirement that
    /// the heuristic's blind spots read as a hint, not an authoritative scan.</summary>
    public required string Reason { get; init; }
}

/// <summary>
/// Best-effort scan for candidate workspace relationships (workspace-intelligence Module 2): for
/// each AKS node in the curated topology, looks at one matching pod's env vars and its namespace's
/// ConfigMaps for a value containing another topology node's resource key. This is deliberately
/// naive substring matching, not a real dependency-graph inference — see the class doc comment on
/// <see cref="WorkspaceRelationshipSuggestion"/> and the UI copy that presents these as suggestions,
/// never as confirmed fact.
/// </summary>
public sealed class WorkspaceRelationshipSuggestionService
{
    private readonly ProfileRepository _profiles;
    private readonly IMonitoringConnectionPool _connectionPool;

    public WorkspaceRelationshipSuggestionService(ProfileRepository profiles, IMonitoringConnectionPool connectionPool)
    {
        _profiles = profiles;
        _connectionPool = connectionPool;
    }

    public async Task<IReadOnlyList<WorkspaceRelationshipSuggestion>> GetSuggestionsAsync(CancellationToken ct)
    {
        var topology = _profiles.Config.Topology;
        var aksNodes = topology.Nodes.Where(n => n.Area == WorkspaceResourceArea.Aks).ToList();
        var otherNodes = topology.Nodes.Where(n => n.Area != WorkspaceResourceArea.Aks).ToList();
        if (aksNodes.Count == 0 || otherNodes.Count == 0)
            return [];

        // Reuses the same cached-client resolution Monitoring's alert engine already uses (demo vs.
        // real AKS config handled once, in one place) — this scan shouldn't need its own connection
        // logic just because it lives in a different feature.
        var client = _connectionPool.GetAksClient();
        if (client is null)
            return [];

        // Either direction — a confirmed A→B relationship should suppress suggesting B→A too.
        var existingPairs = new HashSet<(string, string)>(
            topology.Relationships.SelectMany(r => new[] { (r.FromNodeId, r.ToNodeId), (r.ToNodeId, r.FromNodeId) }));

        var suggestions = new List<WorkspaceRelationshipSuggestion>();

        foreach (var aksNode in aksNodes)
        {
            var parts = aksNode.ResourceKey.Split('/', 2);
            if (parts.Length != 2)
                continue; // not the "namespace/deployment" shape Module 1's AKS candidates use

            var (ns, deployment) = (parts[0], parts[1]);

            List<string> haystack;
            try
            {
                haystack = await CollectHaystackAsync(client, ns, deployment, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Best-effort: a namespace/pod that no longer exists or a transient API error skips
                // this one AKS node's scan rather than failing the whole request.
                continue;
            }

            foreach (var otherNode in otherNodes)
            {
                if (existingPairs.Contains((aksNode.Id, otherNode.Id)))
                    continue;

                var matchFragment = ResourceKeyMatchFragment(otherNode.ResourceKey);
                if (matchFragment is null)
                    continue;

                var isMatch = haystack.Any(value => value.Contains(matchFragment, StringComparison.OrdinalIgnoreCase));
                if (isMatch)
                {
                    suggestions.Add(new WorkspaceRelationshipSuggestion
                    {
                        FromNodeId = aksNode.Id,
                        ToNodeId = otherNode.Id,
                        Reason = $"Pod config in {ns}/{deployment} contains a value matching \"{otherNode.DisplayLabel}\" "
                            + "— based on matching names in pod configuration; may miss or misidentify real relationships.",
                    });
                }
            }
        }

        return suggestions;
    }

    private static async Task<List<string>> CollectHaystackAsync(IAksClient client, string ns, string deployment, CancellationToken ct)
    {
        var haystack = new List<string>();

        var pods = await client.GetPodsAsync(ns, ct: ct);
        var matchingPod = pods.FirstOrDefault(p => p.Name.StartsWith(deployment + "-", StringComparison.OrdinalIgnoreCase));
        if (matchingPod is not null)
        {
            var containers = await client.GetContainerDetailsAsync(ns, matchingPod.Name, ct);
            haystack.AddRange(containers
                .SelectMany(c => c.EnvVars)
                .Select(e => e.Value)
                .Where(v => !string.IsNullOrEmpty(v))!);
        }

        var configMaps = await client.GetConfigMapsAsync(ns, ct);
        haystack.AddRange(configMaps.SelectMany(cm => cm.Data.Values));

        return haystack;
    }

    /// <summary>The substring worth searching for — a Service Bus/Storage resource key sometimes has
    /// a queue/container name appended after a '/' (see <c>WorkspaceResourceNode.ResourceKey</c>'s
    /// doc comment); only the part before it (the actual hostname/account name) is realistically
    /// going to show up verbatim in an env var or ConfigMap value.</summary>
    private static string? ResourceKeyMatchFragment(string resourceKey)
    {
        var fragment = resourceKey.Split('/')[0].Trim();
        return fragment.Length == 0 ? null : fragment;
    }
}
