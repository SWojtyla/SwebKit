using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

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
        var suggestions = new List<WorkspaceRelationshipSuggestion>();

        // Per map, never across maps: a suggested edge only makes sense between nodes the user
        // grouped into the same project graph.
        foreach (var map in _profiles.Config.EffectiveMaps())
        {
            var aksNodes = map.Nodes.Where(n => n.Area == WorkspaceResourceArea.Aks).ToList();
            var otherNodes = map.Nodes.Where(n => n.Area != WorkspaceResourceArea.Aks).ToList();
            if (aksNodes.Count == 0 || otherNodes.Count == 0)
                continue;

            // Either direction — a confirmed A→B relationship should suppress suggesting B→A too.
            var existingPairs = new HashSet<(string, string)>(
                map.Relationships.SelectMany(r => new[] { (r.FromNodeId, r.ToNodeId), (r.ToNodeId, r.FromNodeId) }));

            foreach (var aksNode in aksNodes)
            {
                var parts = aksNode.ResourceKey.Split('/', 2);
                if (parts.Length != 2)
                    continue; // namespace-level nodes have no deployment to scope the scan to

                var (ns, deployment) = (parts[0], parts[1]);

                // Reuses the same cached-client resolution Monitoring's alert engine already uses
                // (demo vs. real AKS config handled once, in one place); the node's own
                // kubeconfig context picks the right pooled client for multi-context maps.
                var client = _connectionPool.GetAksClient(aksNode.KubeconfigContext);
                if (client is null)
                    continue;

                Haystack haystack;
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

                    var matchFragment = ResourceKeyMatchFragment(otherNode);
                    if (matchFragment is null)
                        continue;

                    var configMatch = haystack.ConfigValues.Any(value => value.Contains(matchFragment, StringComparison.OrdinalIgnoreCase));
                    // Config wins over logs for the same pair — a config hit is stronger evidence, and
                    // one suggestion per pair regardless of how many sources matched.
                    var logMatch = !configMatch &&
                        haystack.LogLines.Any(line => line.Contains(matchFragment, StringComparison.OrdinalIgnoreCase));
                    if (configMatch || logMatch)
                    {
                        suggestions.Add(new WorkspaceRelationshipSuggestion
                        {
                            FromNodeId = aksNode.Id,
                            ToNodeId = otherNode.Id,
                            Reason = configMatch
                                ? $"Pod config in {ns}/{deployment} contains a value matching \"{otherNode.DisplayLabel}\" "
                                    + "— based on matching names in pod configuration; may miss or misidentify real relationships."
                                : $"Recent pod logs in {ns}/{deployment} mention \"{otherNode.DisplayLabel}\" "
                                    + "— weaker evidence than configuration (names can appear in error text); confirm before accepting.",
                        });
                    }
                }
            }
        }

        return suggestions;
    }

    /// <summary>Config values (env vars + ConfigMaps) and recent pod-log lines as separate
    /// haystacks — a log hit produces a differently-worded, lower-confidence Reason than a config
    /// hit, so the two sources can't be merged into one list.</summary>
    private sealed record Haystack(List<string> ConfigValues, List<string> LogLines);

    /// <summary>Cap on log lines collected per pod — recent lines are what matter (a crashlooping
    /// pod surfaces the resource it was trying to reach in its last lines), and the suggestion
    /// scan shouldn't read an unbounded log stream.</summary>
    private const int MaxLogLines = 100;

    private static async Task<Haystack> CollectHaystackAsync(IAksClient client, string ns, string deployment, CancellationToken ct)
    {
        var configValues = new List<string>();
        var logLines = new List<string>();

        var pods = await client.GetPodsAsync(ns, ct: ct);
        var matchingPod = pods.FirstOrDefault(p => p.Name.StartsWith(deployment + "-", StringComparison.OrdinalIgnoreCase));
        if (matchingPod is not null)
        {
            var containers = await client.GetContainerDetailsAsync(ns, matchingPod.Name, ct);
            configValues.AddRange(containers
                .SelectMany(c => c.EnvVars)
                .Select(e => e.Value)
                .Where(v => !string.IsNullOrEmpty(v))!);

            // agent-correlation Module 6: recent logs are a second haystack — a pod whose config
            // only names a Secret reference (which the scan can't read) still names the resource
            // it was trying to reach in its log lines.
            try
            {
                var opts = new LogStreamOptions { TailLines = MaxLogLines, Follow = false };
                await foreach (var line in client.StreamPodLogsAsync(ns, matchingPod.Name, container: string.Empty, opts, ct))
                    logLines.Add(line);
            }
            catch (Exception)
            {
                // Best-effort: an unreadable log stream (pod mid-restart, container not yet
                // started) just contributes no log haystack — it must not sink the config scan.
            }
        }

        var configMaps = await client.GetConfigMapsAsync(ns, ct);
        configValues.AddRange(configMaps.SelectMany(cm => cm.Data.Values));

        return new Haystack(configValues, logLines);
    }

    /// <summary>The substring worth searching for — a Service Bus/Storage resource key sometimes has
    /// a queue/container name appended after a '/' (see <c>WorkspaceResourceNode.ResourceKey</c>'s
    /// doc comment); only the part before it (the actual hostname/account name) is realistically
    /// going to show up verbatim in an env var or ConfigMap value.</summary>
    private static string? ResourceKeyMatchFragment(WorkspaceResourceNode node)
    {
        var fragment = node.ResourceKey.Split('/')[0].Trim();
        // SQL servers surface in pod config as either the FQDN (connection strings) or just the
        // short server name — the first DNS label is a substring of the FQDN, so it catches both.
        if (node.Area == WorkspaceResourceArea.Sql)
        {
            fragment = fragment.Split('.')[0];
            // A very short server name would false-positive on unrelated values — skip it.
            if (fragment.Length < 4)
                return null;
        }
        return fragment.Length == 0 ? null : fragment;
    }
}
