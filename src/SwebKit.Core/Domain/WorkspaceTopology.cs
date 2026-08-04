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
}

/// <summary>A user-curated node in the workspace topology graph — a specific resource (an AKS
/// deployment, a Service Bus namespace, a Redis cache, a Storage account) the user has declared as
/// relevant, whether or not it was picked from an auto-populated candidate.</summary>
public class WorkspaceResourceNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public WorkspaceResourceArea Area { get; set; }

    /// <summary>Free-text reference to the concrete resource — e.g. "prod-ns/api-deployment" for
    /// AKS, a Service Bus namespace's fully-qualified hostname (optionally with a queue/topic name
    /// appended), a Redis cache id, or a storage account name (optionally with a container
    /// appended). Auto-populated candidates fill this at the resource-level granularity available
    /// from existing config; the user can refine it further by hand (e.g. append "/orders-queue").</summary>
    public string ResourceKey { get; set; } = string.Empty;

    public string DisplayLabel { get; set; } = string.Empty;
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

/// <summary>Persisted, per-profile workspace topology: the set of nodes and relationships the user
/// has curated. Same lifecycle as <see cref="AppConfig.AksConfig"/>/<see cref="AppConfig.RedisConfig"/>/
/// <see cref="AppConfig.StorageAccounts"/> — round-trips through the same profile save/export/import
/// paths with no separate persistence mechanism.</summary>
public class WorkspaceTopology
{
    public List<WorkspaceResourceNode> Nodes { get; set; } = [];
    public List<WorkspaceResourceRelationship> Relationships { get; set; } = [];
}

/// <summary>A not-yet-added node the user can pick from, computed on demand from what's already
/// configured (AKS namespaces/deployments, Service Bus namespaces, Redis caches, Storage accounts)
/// rather than persisted itself — see <c>WorkspaceTopologyEndpoints.GetCandidatesAsync</c>.</summary>
public class WorkspaceResourceCandidate
{
    public WorkspaceResourceArea Area { get; set; }
    public string ResourceKey { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
}
