using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Agents;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Endpoints;

public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this WebApplication app)
    {
        // ── Send message ────────────────────────────────────────────────────────

        app.MapPost("/api/agent/chat", ChatAsync);

        // ── Clear history ───────────────────────────────────────────────────────

        app.MapPost("/api/agent/clear", ClearHistory);

        // ── Get status ──────────────────────────────────────────────────────────

        app.MapGet("/api/agent/status", GetStatus);

        // ── Capability test ─────────────────────────────────────────────────────

        app.MapPost("/api/agent/profiles/{id}/test", TestProfileAsync);

        // ── Pending actions (confirm-before-execute) ────────────────────────────

        app.MapGet("/api/agent/pending-approvals", GetPendingApprovals);
        app.MapPost("/api/agent/pending-approvals/{id}/confirm", ConfirmActionAsync);
        app.MapPost("/api/agent/pending-approvals/{id}/reject", RejectAction);
    }

    internal static async Task<IResult> ChatAsync(
        SidecarAgentChatService agent,
        AgentChatRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return Results.BadRequest("Message is required");

        var reply = await agent.SendAsync(req.SessionId, req.Message, req.Context, req.Mode, ct);
        return Results.Ok(reply);
    }

    internal static Ok<object> ClearHistory(SidecarAgentChatService agent, string? sessionId = null)
    {
        agent.ClearHistory(sessionId);
        return TypedResults.Ok<object>(new { cleared = true });
    }

    internal static Ok<object> GetStatus(SidecarAgentChatService agent, string? sessionId = null)
    {
        return TypedResults.Ok<object>(new
        {
            historyCount = agent.GetHistoryCount(sessionId),
        });
    }

    /// <summary>
    /// Runs <see cref="AgentCapabilityTester"/> against the named profile and returns the result.
    /// Stateless — does not persist the result onto the profile. The frontend already round-trips
    /// the whole <c>UserSettings</c> blob via <c>PUT /api/config/user-settings</c> for every other
    /// profile edit; it patches <c>capability</c>/<c>lastTestDiagnostic</c> into its local state from
    /// this response and saves through that same existing path rather than this endpoint owning a
    /// second, parallel persistence mechanism.
    /// </summary>
    internal static async Task<IResult> TestProfileAsync(
        string id,
        AgentCapabilityTester tester,
        SwebKit.Core.Configuration.UserSettingsRepository settings,
        CancellationToken ct)
    {
        var profile = settings.Settings.Agent.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
            return Results.NotFound();

        var result = await tester.TestAsync(profile, ct);
        return Results.Ok(result);
    }

    /// <summary>Lists actions currently awaiting user confirmation. Deliberately doesn't expose
    /// <see cref="PendingAgentAction.Payload"/> — that's an internal detail for the executor
    /// applying the action, not something the confirm-card UI needs.</summary>
    internal static Ok<IReadOnlyList<PendingActionSummary>> GetPendingApprovals(IAgentActionCoordinator coordinator)
    {
        var summaries = coordinator.GetPendingActions()
            .Select(a => new PendingActionSummary
            {
                Id = a.Id,
                Type = a.Type.ToString(),
                Summary = a.Summary,
                Target = a.Target,
                Risk = a.Risk.ToString(),
                Preview = a.Preview,
                ExpiresAt = a.ExpiresAt,
            })
            .ToList();
        return TypedResults.Ok<IReadOnlyList<PendingActionSummary>>(summaries);
    }

    internal static async Task<IResult> ConfirmActionAsync(string id, IAgentActionCoordinator coordinator, AgentActionApplier applier, CancellationToken ct)
    {
        var action = coordinator.GetAction(id);
        if (action is null)
            return Results.NotFound();

        action.Confirm();
        var result = await applier.ApplyAsync(id, ct);
        return TypedResults.Ok(result);
    }

    internal static IResult RejectAction(string id, IAgentActionCoordinator coordinator)
    {
        var action = coordinator.GetAction(id);
        if (action is null)
            return Results.NotFound();

        coordinator.RejectAction(id);
        return TypedResults.Ok<object>(new { rejected = true });
    }
}

public sealed class PendingActionSummary
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Summary { get; init; }
    public required string Target { get; init; }
    public required string Risk { get; init; }
    public required string Preview { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed class AgentChatRequest
{
    public string Message { get; set; } = string.Empty;

    /// <summary>Scopes this conversation's history to a contextual assistant panel instance
    /// (client-generated, see <c>useContextualAgent</c>). Null/omitted keeps using the single
    /// global session the pre-Module-2 <c>/agent</c> page always used.</summary>
    public string? SessionId { get; set; }

    /// <summary>What the user currently has open (feature area + selection) — populated by a
    /// contextual assistant panel, null for the global <c>/agent</c> page.</summary>
    public AgentChatContext? Context { get; set; }

    /// <summary>"ask" or "ask_and_do". Anything else (including null/omitted) is treated as "ask" —
    /// see <c>SidecarAgentChatService</c>'s doc comment for why an unrecognized value never
    /// silently grants the more permissive mode.</summary>
    public string? Mode { get; set; }
}
