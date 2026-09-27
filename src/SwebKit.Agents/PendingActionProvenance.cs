namespace SwebKit.Agents;

/// <summary>
/// Provenance stamp for a <see cref="PendingAgentAction"/>, captured from the ambient
/// <see cref="AgentExecutionContext"/> selection the proactive-investigation runner pushes
/// around each tool call (monitoring-closed-loop 1c).
///
/// A <c>propose_*</c> tool running inside a background investigation sees
/// <c>{"origin": "investigation", "session_id": "proactive-{ruleId}-{firedAtMs}"}</c> in the
/// selection; interactive chat turns carry no such markers, so the same tool parks an
/// un-stamped action with the interactive lifetime. Proposals parked by an unsupervised run get
/// <see cref="InvestigationLifetime"/> (24h) instead of the chat-sized 5-minute default — the
/// user reads the report hours later and the card must still be confirmable.
/// </summary>
public static class PendingActionProvenance
{
    /// <summary><see cref="AgentExecutionContext"/> selection key carrying the run origin.</summary>
    public const string OriginKey = "origin";

    /// <summary>Selection key carrying the originating session/report id.</summary>
    public const string SessionIdKey = "session_id";

    /// <summary>Origin value stamped by a background proactive investigation.</summary>
    public const string InvestigationOrigin = "investigation";

    /// <summary>Expiry for investigation-parked actions — sized so a report's confirm cards are
    /// still live when the user opens it the next day.</summary>
    public static readonly TimeSpan InvestigationLifetime = TimeSpan.FromHours(24);

    /// <summary>The chat-sized default — same 5 minutes <see cref="PendingAgentAction.ExpiresAt"/>
    /// already uses.</summary>
    public static readonly TimeSpan InteractiveLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Ambient origin value (e.g. <see cref="InvestigationOrigin"/>), or null in an
    /// interactive turn / outside any pushed selection.</summary>
    public static string? AmbientOrigin =>
        AgentExecutionContext.Selection is { } selection
        && selection.TryGetValue(OriginKey, out var origin)
        && !string.IsNullOrWhiteSpace(origin)
            ? origin
            : null;

    /// <summary>Ambient origin session id, or null when absent.</summary>
    public static string? AmbientSessionId =>
        AgentExecutionContext.Selection is { } selection
        && selection.TryGetValue(SessionIdKey, out var sessionId)
        && !string.IsNullOrWhiteSpace(sessionId)
            ? sessionId
            : null;

    /// <summary>The expiry a proposal parked right now should carry — extended for
    /// investigation origin, the interactive default otherwise.</summary>
    public static DateTimeOffset ExpiryForAmbient(DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.UtcNow;
        return at + (AmbientOrigin == InvestigationOrigin ? InvestigationLifetime : InteractiveLifetime);
    }
}
