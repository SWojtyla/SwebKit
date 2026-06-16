using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Repository for API Client flows (local and linked-root storage).
/// </summary>
public interface IApiFlowRepository
{
    // ─── Local Flows ─────────────────────────────────────────────────────────

    /// <summary>Gets all locally stored flows.</summary>
    IReadOnlyList<ApiFlowDefinition> LocalFlows { get; }

    /// <summary>Loads local flows from disk.</summary>
    Task LoadLocalAsync();

    /// <summary>Saves local flows to disk.</summary>
    Task SaveLocalAsync();

    /// <summary>Adds a new local flow.</summary>
    Task<ApiFlowDefinition> AddLocalFlowAsync(ApiFlowDefinition flow);

    /// <summary>Updates an existing local flow.</summary>
    Task<bool> UpdateLocalFlowAsync(ApiFlowDefinition flow);

    /// <summary>Deletes a local flow by ID.</summary>
    Task<bool> DeleteLocalFlowAsync(string flowId);

    /// <summary>Renames a local flow (updates Name only).</summary>
    Task<bool> RenameLocalFlowAsync(string flowId, string newName);

    // ─── Linked-Root Flows ───────────────────────────────────────────────────

    /// <summary>Gets all flows stored in linked roots.</summary>
    Task<List<ApiFlowDefinition>> GetLinkedRootFlowsAsync();

    /// <summary>Gets flows for a specific linked root.</summary>
    Task<List<ApiFlowDefinition>> GetLinkedRootFlowsAsync(string linkedRootId);

    /// <summary>Adds a new flow to a linked root.</summary>
    Task<ApiFlowDefinition> AddLinkedRootFlowAsync(string linkedRootId, ApiFlowDefinition flow);

    /// <summary>Updates an existing linked-root flow.</summary>
    Task<bool> UpdateLinkedRootFlowAsync(ApiFlowDefinition flow);

    /// <summary>Deletes a linked-root flow by ID.</summary>
    Task<bool> DeleteLinkedRootFlowAsync(string flowId, string linkedRootId);

    /// <summary>Renames a linked-root flow (updates Name only).</summary>
    Task<bool> RenameLinkedRootFlowAsync(string flowId, string linkedRootId, string newName);

    /// <summary>Gets a specific linked-root flow by ID.</summary>
    Task<ApiFlowDefinition?> GetLinkedRootFlowByIdAsync(string flowId, string linkedRootId);

    // ─── All Flows ───────────────────────────────────────────────────────────

    /// <summary>Gets all flows (local + linked-root).</summary>
    Task<List<ApiFlowDefinition>> GetAllFlowsAsync();

    /// <summary>Gets a flow by ID, regardless of storage scope.</summary>
    Task<ApiFlowDefinition?> GetFlowByIdAsync(string flowId);

    /// <summary>
    /// Creates a flow with collision-safe naming (appends suffix if name exists).
    /// </summary>
    Task<ApiFlowDefinition> CreateFlowWithUniqueNameAsync(
        string name,
        ApiFlowStorageScope scope,
        string? linkedRootId = null);
}
