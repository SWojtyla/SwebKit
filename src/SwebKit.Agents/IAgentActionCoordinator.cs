using System.Text.Json;
using SwebKit.Agents.Tools;

namespace SwebKit.Agents;

/// <summary>
/// Type of action proposed by the agent.
/// </summary>
public enum AgentActionType
{
    CreateRequest,
    UpdateRequest,
    DeleteRequest,
    DuplicateRequest,
    MoveRequest,
    RenameFolder,
    DeleteFolder,
    ExecuteHttpRequest,
}

/// <summary>
/// Risk level for a proposed action.
/// </summary>
public enum AgentActionRisk
{
    None,
    Low,
    High,
}

/// <summary>
/// A pending action awaiting user confirmation.
/// Stored in memory with expiration and fingerprint for freshness validation.
/// </summary>
public sealed class PendingAgentAction
{
    private readonly object _stateLock = new();
    private bool _isConfirmed;
    private bool _isRejected;
    private bool _isApplied;

    public required string Id { get; init; }
    public required AgentActionType Type { get; init; }
    public required string Summary { get; init; }
    public required string Target { get; init; }
    public required AgentActionRisk Risk { get; init; }
    public required string Preview { get; init; }
    public required string? ExpectedFingerprint { get; init; }

    /// <summary>
    /// The structured tool-call arguments behind this proposal (e.g. the exact <c>operation</c>/
    /// <c>request_id</c>/<c>name</c>/<c>method</c>/<c>url</c> fields <c>ProposeApiRequestChangeTool</c>
    /// received), so the executor applying this action can act on exact values instead of parsing
    /// the human-readable <see cref="Preview"/> string. Null for action types that don't need it
    /// (e.g. <see cref="AgentActionType.DeleteRequest"/> only needs <see cref="Target"/>).
    /// </summary>
    public JsonElement? Payload { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.UtcNow.AddMinutes(5);
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    public bool IsConfirmed { get { lock (_stateLock) return _isConfirmed; } }
    public bool IsRejected { get { lock (_stateLock) return _isRejected; } }
    public bool IsApplied { get { lock (_stateLock) return _isApplied; } }

    public void Confirm() { lock (_stateLock) _isConfirmed = true; }
    public void Reject() { lock (_stateLock) _isRejected = true; }
    public void MarkApplied() { lock (_stateLock) _isApplied = true; }
}

/// <summary>
/// Result of applying a confirmed action.
/// </summary>
public sealed class AgentActionResult
{
    public required bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ResultSummary { get; init; }
}

/// <summary>
/// Coordinates proposal, confirmation, and application of agent actions.
/// Stores pending actions in a bounded in-memory store with expiration.
/// </summary>
public interface IAgentActionCoordinator
{
    /// <summary>Stores a new pending action and returns its ID.</summary>
    string RegisterAction(PendingAgentAction action);

    /// <summary>Retrieves a pending action by ID. Returns null if not found or expired.</summary>
    PendingAgentAction? GetAction(string actionId);

    /// <summary>Returns all non-expired, non-applied, non-rejected pending actions.</summary>
    IReadOnlyList<PendingAgentAction> GetPendingActions();

    /// <summary>Rejects a pending action.</summary>
    void RejectAction(string actionId);

    /// <summary>Removes expired actions from the store.</summary>
    void CleanupExpired();

    /// <summary>Maximum number of pending actions kept in memory.</summary>
    int MaxPendingActions { get; }
}

/// <summary>
/// In-memory implementation of <see cref="IAgentActionCoordinator"/>.
/// </summary>
public sealed class AgentActionCoordinator : IAgentActionCoordinator
{
    private readonly Dictionary<string, PendingAgentAction> _actions = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public int MaxPendingActions => 10;

    public string RegisterAction(PendingAgentAction action)
    {
        lock (_lock)
        {
            // Enforce bounded store
            if (_actions.Count >= MaxPendingActions)
            {
                // Remove oldest expired or oldest overall
                var toRemove = _actions.Values
                    .OrderBy(a => a.CreatedAt)
                    .First();
                _actions.Remove(toRemove.Id);
            }

            _actions[action.Id] = action;
            return action.Id;
        }
    }

    public PendingAgentAction? GetAction(string actionId)
    {
        lock (_lock)
        {
            if (!_actions.TryGetValue(actionId, out var action))
                return null;

            if (action.IsExpired)
            {
                _actions.Remove(actionId);
                return null;
            }

            return action;
        }
    }

    public IReadOnlyList<PendingAgentAction> GetPendingActions()
    {
        lock (_lock)
        {
            return _actions.Values
                .Where(a => !a.IsExpired && !a.IsApplied && !a.IsRejected)
                .OrderBy(a => a.CreatedAt)
                .ToList();
        }
    }

    public void RejectAction(string actionId)
    {
        lock (_lock)
        {
            if (_actions.TryGetValue(actionId, out var action))
            {
                action.Reject();
                _actions.Remove(actionId);
            }
        }
    }

    public void CleanupExpired()
    {
        lock (_lock)
        {
            var expired = _actions.Values.Where(a => a.IsExpired).Select(a => a.Id).ToList();
            foreach (var id in expired)
                _actions.Remove(id);
        }
    }
}
