namespace SwebKit.Core.Domain;

// ─── API request chains (api-request-chains) ─────────────────────────────────

/// <summary>
/// A named, ordered, re-runnable sequence of requests that may span collections —
/// the persisted entity behind run mode <c>"chain"</c>. Chains live only in the internal
/// store (<c>chains.json</c>); they are never written into linked roots, though their steps
/// may point at linked-root requests.
/// </summary>
/// <remarks>
/// Steps are stored verbatim — a step whose collection or request no longer resolves is a
/// plan error (<c>unknown_request</c>) at run time, never silently dropped here.
/// </remarks>
public sealed class ApiChain
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<ApiChainStep> Steps { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One row of a chain — a pointer at a request inside a specific collection.</summary>
public sealed class ApiChainStep
{
    /// <summary>Stable step id — correlates <c>stepId</c>/<c>ownerStepId</c> on run SSE events back to this row.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Internal or linked-root collection id the request resolves in.</summary>
    public string CollectionId { get; set; } = string.Empty;
    /// <summary>Set when the collection lives under a linked root.</summary>
    public string? LinkedRootId { get; set; }
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Disabled steps stay on the chain but expand to nothing at plan time.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>Root object stored in <c>chains.json</c>.</summary>
public sealed class ChainsStore
{
    public int SchemaVersion { get; set; } = 1;
    public List<ApiChain> Chains { get; set; } = [];
}
