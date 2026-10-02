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
    DeleteRedisKey,
    SetRedisKeyTtl,
    CopyBlob,
    ExecuteSql,
    ApplyAksYaml,
    CreateAlertRule,

    /// <summary>A mutating call to a user-configured external MCP server (agent-mcp-evolution
    /// Phase 2b) — payload carries the serialized <c>AgentMcpServer</c> config, remote tool name,
    /// and arguments; the executor re-dials the cached connection on confirm.</summary>
    ExternalMcpCall,

    /// <summary>Restart an AKS deployment (kubectl rollout restart) — monitoring-closed-loop
    /// autofix proposal; applied by <c>AksActionExecutor</c>. Payload: namespace, deployment,
    /// context.</summary>
    RestartAksDeployment,

    /// <summary>Delete an AKS pod (the controller recreates it) — monitoring-closed-loop autofix
    /// proposal; applied by <c>AksActionExecutor</c>. Payload: namespace, pod, context.</summary>
    DeleteAksPod,

    /// <summary>Purge every message in a Service Bus entity's dead-letter sub-queue —
    /// monitoring-closed-loop autofix proposal; applied by the sidecar's
    /// <c>ServiceBusActionExecutor</c>. Payload: namespace, entity_path.</summary>
    PurgeServiceBusDeadLetters,

    /// <summary>Resubmit specific dead-lettered Service Bus messages by sequence number —
    /// monitoring-closed-loop autofix proposal; applied by the sidecar's
    /// <c>ServiceBusActionExecutor</c>. Payload: namespace, entity_path, sequence_numbers,
    /// optional target_entity_path.</summary>
    ResubmitServiceBusDeadLetters,

    /// <summary>Flush a Redis database — monitoring-closed-loop autofix proposal; applied by
    /// <c>RedisActionExecutor</c>. Payload: cache_id.</summary>
    FlushRedisDatabase,

    /// <summary>Upsert a collection variable (value or generator like <c>guid</c>) —
    /// api-client-agent-variables; applied by <c>ApiClientActionExecutor</c>. Payload:
    /// collection_id, key, value, generator, enabled.</summary>
    SetCollectionVariable,
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

    /// <summary>
    /// What proposed this action (monitoring-closed-loop): null for an interactive chat turn,
    /// <see cref="PendingActionProvenance.InvestigationOrigin"/> ("investigation") for a background
    /// proactive investigation. Stamped by the proposing tool from the ambient
    /// <see cref="AgentExecutionContext"/> selection the runner pushes around each tool call.
    /// Investigation proposals carry a much longer expiry — nobody is watching the turn that
    /// parked them, so the interactive 5-minute default would leave them dead on arrival.
    /// </summary>
    public string? Origin { get; set; }

    /// <summary>The originating session/report id when <see cref="Origin"/> is set (e.g.
    /// <c>proactive-{ruleId}-{firedAtMs}</c>) — the linkage a persisted insight report uses to
    /// render this proposal's confirm card hours later.</summary>
    public string? OriginSessionId { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(5);
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

    /// <summary>Live pending actions stamped with a given <see cref="PendingAgentAction.OriginSessionId"/>
    /// — the per-investigation proposal cap (<c>ProposalParking.MaxPerInvestigation</c>) counts
    /// these so a runaway model can't park unbounded cards from one run.</summary>
    int CountPendingByOrigin(string originSessionId);

    /// <summary>Rejects a pending action.</summary>
    void RejectAction(string actionId);

    /// <summary>Removes expired actions from the store.</summary>
    void CleanupExpired();

    /// <summary>Maximum number of pending actions kept in memory.</summary>
    int MaxPendingActions { get; }

    /// <summary>Raised synchronously each time a genuinely new action is parked — monitoring-
    /// closed-loop's proactive-insight service correlates investigation proposals to their
    /// report via <see cref="PendingAgentAction.OriginSessionId"/>. Deduplicated registrations
    /// (same origin session, type and target) do not fire.</summary>
    event Action<PendingAgentAction>? ActionRegistered;
}

/// <summary>
/// In-memory implementation of <see cref="IAgentActionCoordinator"/>.
/// </summary>
public sealed class AgentActionCoordinator : IAgentActionCoordinator
{
    private readonly Dictionary<string, PendingAgentAction> _actions = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public int MaxPendingActions => 10;

    public event Action<PendingAgentAction>? ActionRegistered;

    public string RegisterAction(PendingAgentAction action)
    {
        lock (_lock)
        {
            // Dedupe: a background investigation that emits the same proposal twice parks one
            // action, not two near-identical confirm cards — the second registration returns the
            // existing id (and deliberately does not fire ActionRegistered again).
            if (action.OriginSessionId is { Length: > 0 } originSessionId)
            {
                var existing = _actions.Values.FirstOrDefault(a =>
                    !a.IsExpired && !a.IsApplied && !a.IsRejected
                    && a.OriginSessionId == originSessionId
                    && a.Type == action.Type
                    && a.Target == action.Target);
                if (existing is not null)
                    return existing.Id;
            }

            // Enforce bounded store. Eviction prefers the oldest expired entry, then the oldest
            // interactive (origin-less) proposal — investigation proposals parked by an
            // unsupervised run must not be silently evicted by chat traffic before the user ever
            // sees them (monitoring-closed-loop).
            if (_actions.Count >= MaxPendingActions)
            {
                var toRemove = _actions.Values
                    .Where(a => a.IsExpired)
                    .OrderBy(a => a.CreatedAt)
                    .FirstOrDefault()
                    ?? _actions.Values
                        .Where(a => a.Origin is null)
                        .OrderBy(a => a.CreatedAt)
                        .FirstOrDefault()
                    ?? _actions.Values
                        .OrderBy(a => a.CreatedAt)
                        .First();
                _actions.Remove(toRemove.Id);
            }

            _actions[action.Id] = action;
        }

        // Raise outside the lock — subscribers (e.g. the proactive-insight service) must not be
        // able to deadlock the store, and event ordering after insertion is guaranteed anyway.
        try
        {
            ActionRegistered?.Invoke(action);
        }
        catch
        {
            // A subscriber failure must not break the proposing tool's own result path.
        }
        return action.Id;
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

    public int CountPendingByOrigin(string originSessionId)
    {
        lock (_lock)
        {
            return _actions.Values.Count(a =>
                !a.IsExpired && !a.IsApplied && !a.IsRejected
                && a.OriginSessionId == originSessionId);
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
