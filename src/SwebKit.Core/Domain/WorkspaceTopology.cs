namespace SwebKit.Core.Domain;

/// <summary>Feature area a <see cref="WorkspaceResourceNode"/> belongs to. Deliberately the same
/// four areas <see cref="FeatureArea"/> covers for agent tools (minus Observability, which is
/// cross-cutting diagnostic data, not a resource you'd place a topology node on).</summary>
public enum WorkspaceResourceArea
{
    Aks,
    ServiceBus,
    Redis,
    Storage,
    Sql,
}

/// <summary>A user-curated node in a workspace map — a specific resource (an AKS
/// deployment, a Service Bus namespace, a Redis cache, a Storage account) the user has declared as
/// relevant, whether or not it was picked from an auto-populated candidate.</summary>
public class WorkspaceResourceNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public WorkspaceResourceArea Area { get; set; }

    /// <summary>Free-text reference to the concrete resource — e.g. "prod-ns/api-deployment" or a
    /// bare "prod-ns" (namespace-level node) for AKS, a Service Bus namespace's fully-qualified
    /// hostname (optionally with a queue/topic name appended), a Redis cache id, or a storage
    /// account name (optionally with a container appended). Auto-populated candidates fill this at
    /// the resource-level granularity available from existing config; the user can refine it
    /// further by hand (e.g. append "/orders-queue").</summary>
    public string ResourceKey { get; set; } = string.Empty;

    public string DisplayLabel { get; set; } = string.Empty;

    /// <summary>Optional kubeconfig context for AKS nodes — the same string
    /// <see cref="AksConfig.KubeconfigContext"/>/<c>AksPodAlertParams.KubeconfigContext</c> carry.
    /// Null/empty means "whatever context is globally configured" (also the state of every node
    /// persisted before multi-context maps existed). Only meaningful when
    /// <see cref="Area"/> is <see cref="WorkspaceResourceArea.Aks"/> — two maps can pin the same
    /// namespace name on different clusters without the alert-investigation matcher confusing them.</summary>
    public string? KubeconfigContext { get; set; }
}

/// <summary>A user-declared relationship between two <see cref="WorkspaceResourceNode"/>s (e.g.
/// "consumes", "caches into", "writes to"). Never inferred automatically in this module — see
/// Module 2 of workspace-intelligence's technical-plan.md for the additive, confirm-first heuristic
/// suggestion feature this deliberately does not implement yet.</summary>
public class WorkspaceResourceRelationship
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string FromNodeId { get; set; } = string.Empty;
    public string ToNodeId { get; set; } = string.Empty;
    public string? Label { get; set; }
}

/// <summary>The graph shape every workspace map has: nodes plus the relationships between them.
/// Same lifecycle as <see cref="AppConfig.AksConfig"/>/<see cref="AppConfig.RedisConfig"/>/
/// <see cref="AppConfig.StorageAccounts"/> — round-trips through the same profile save/export/import
/// paths with no separate persistence mechanism.</summary>
public class WorkspaceTopology
{
    public List<WorkspaceResourceNode> Nodes { get; set; } = [];
    public List<WorkspaceResourceRelationship> Relationships { get; set; } = [];
}

/// <summary>One named workspace map — a project/environment's own component graph. A profile can
/// carry several (<see cref="AppConfig.Maps"/>): "each project has its own relationships", so an
/// alert rule's fired resource auto-matches against every map and the one containing it scopes the
/// investigation's relationship walk and prompt section. Inherits <see cref="WorkspaceTopology"/>
/// so every helper taking the plain graph shape (walkers, filters, prompt rendering) accepts a map
/// as-is.</summary>
public class WorkspaceMap : WorkspaceTopology
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
}

/// <summary>The shared "which map/node does this resource belong to" lookup used by both the
/// proactive-insight pipeline (fired alert → starting resource) and
/// <c>investigate_workspace_issue</c> (model/tool call → starting resource). Matching is the same
/// substring-on-key-or-label approach both callers already used; <paramref name="kubeContext"/>
/// adds the multi-context disambiguation — a node pinned to a different context is a different
/// cluster's resource and must not match.</summary>
public static class WorkspaceMapLookup
{
    /// <summary>Searches every map for a node matching (area, hint). When
    /// <paramref name="kubeContext"/> is given, nodes pinned to a different context are excluded and
    /// context-exact matches win over unscoped (context-less) ones; without it any node matches.
    /// Returns the map that owns the matched node, not just the node — the caller walks/injects
    /// that map's relationships, not some merged graph.</summary>
    public static (WorkspaceMap Map, WorkspaceResourceNode Node)? FindNode(
        IEnumerable<WorkspaceMap> maps, WorkspaceResourceArea area, string hint, string? kubeContext = null)
    {
        var candidates = maps
            .SelectMany(m => m.Nodes
                .Where(n => n.Area == area &&
                    (n.ResourceKey.Contains(hint, StringComparison.OrdinalIgnoreCase) ||
                     n.DisplayLabel.Contains(hint, StringComparison.OrdinalIgnoreCase)))
                .Select(n => (Map: m, Node: n)))
            .ToList();

        if (candidates.Count == 0)
            return null;

        if (string.IsNullOrWhiteSpace(kubeContext))
            return candidates[0];

        var exact = candidates.FirstOrDefault(c =>
            string.Equals(c.Node.KubeconfigContext, kubeContext, StringComparison.OrdinalIgnoreCase));
        if (exact.Node is not null)
            return exact;

        // No context-pinned match — an unscoped node ("follows the configured context") is still a
        // valid match; a node pinned to a different context is not.
        var unscoped = candidates.FirstOrDefault(c => string.IsNullOrWhiteSpace(c.Node.KubeconfigContext));
        return unscoped.Node is null ? null : unscoped;
    }
}

/// <summary>A not-yet-added node the user can pick from, computed on demand from what's already
/// configured (AKS namespaces/deployments, Service Bus namespaces, Redis caches, Storage accounts)
/// rather than persisted itself — see <c>WorkspaceTopologyEndpoints.GetCandidatesAsync</c>.</summary>
public class WorkspaceResourceCandidate
{
    public WorkspaceResourceArea Area { get; set; }
    public string ResourceKey { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;

    /// <summary>Same semantics as <see cref="WorkspaceResourceNode.KubeconfigContext"/> — carried
    /// through so adding an AKS candidate preserves the context it was discovered under.</summary>
    public string? KubeconfigContext { get; set; }
}
