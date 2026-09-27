namespace SwebKit.Agents.Tools;

/// <summary>
/// Shared parking path for the monitoring-closed-loop remediation <c>propose_*</c> tools:
/// stamps ambient provenance (<see cref="PendingActionProvenance"/>) onto the action, enforces
/// the per-investigation proposal cap, then registers it with the coordinator.
///
/// The cap matters because a background run is unsupervised — a looping model could otherwise
/// fill the bounded pending store (and the report it links to) with cards nobody asked for.
/// Interactive turns have no ambient origin session, so the cap never applies to them.
/// </summary>
internal static class ProposalParking
{
    /// <summary>Max proposals one background investigation may park (monitoring-closed-loop:
    /// "cap proposals per run at 3").</summary>
    public const int MaxPerInvestigation = 3;

    /// <summary>Stamps the action with ambient provenance (origin, origin session id, the
    /// matching expiry), enforces the per-investigation cap, and registers it.
    /// Returns the parked action id, or null with <paramref name="error"/> set when the cap
    /// rejected the proposal — the tool returns that error JSON to the model, which also
    /// naturally stops it from proposing more.</summary>
    public static string? Park(
        IAgentActionCoordinator coordinator,
        PendingAgentAction action,
        out string? error)
    {
        action.Origin = PendingActionProvenance.AmbientOrigin;
        action.OriginSessionId = PendingActionProvenance.AmbientSessionId;
        action.ExpiresAt = PendingActionProvenance.ExpiryForAmbient();

        if (action.Origin == PendingActionProvenance.InvestigationOrigin
            && action.OriginSessionId is { Length: > 0 } sessionId
            && coordinator.CountPendingByOrigin(sessionId) >= MaxPerInvestigation)
        {
            error = $"Proposal limit reached: this investigation already parked {MaxPerInvestigation} pending actions. Summarize any further findings in the report instead of proposing more.";
            return null;
        }

        error = null;
        return coordinator.RegisterAction(action);
    }
}
