using System.Text.Json;
using System.Text.Json.Serialization;
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

        // ── Send message (streaming, Server-Sent Events) ────────────────────────

        app.MapPost("/api/agent/chat/stream", ChatStreamAsync);

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

        var reply = await agent.SendAsync(req.SessionId, req.Message, req.Context, req.Mode, req.Scope, ct);
        return Results.Ok(reply);
    }

    private static readonly JsonSerializerOptions StreamEventJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Same request shape as <see cref="ChatAsync"/>, but emits one <c>data: {...}\n\n</c> line per
    /// <see cref="AgentStreamEvent"/> as the agentic loop produces it (Server-Sent Events — no
    /// separate client library needed, and both LM Studio's and cloud providers' streaming APIs
    /// already speak this format on the upstream side). Browsers' built-in <c>EventSource</c> can't
    /// send a POST body, so the frontend reads this with a plain <c>fetch</c> + stream reader
    /// instead — see <c>streamAgentChat</c> in <c>web/src/lib/api.ts</c>.
    /// </summary>
    internal static async Task ChatStreamAsync(
        HttpContext httpContext,
        SidecarAgentChatService agent,
        AgentChatRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        httpContext.Response.Headers.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        // Disables response buffering on reverse proxies that respect it (nginx); a no-op, harmless
        // header when running behind Tauri's direct localhost connection like this app does.
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var evt in agent.SendStreamAsync(req.SessionId, req.Message, req.Context, req.Mode, req.Scope, ct))
        {
            var json = JsonSerializer.Serialize(ToWireEvent(evt), StreamEventJsonOptions);
            await httpContext.Response.WriteAsync($"data: {json}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
        }
    }

    /// <summary>
    /// Maps the low-level <see cref="AgentStreamEvent"/> (whose <c>Result</c> is the provider-agnostic
    /// <see cref="AgentChatResult"/> — <c>Elapsed</c> as a <see cref="TimeSpan"/>, no status/error
    /// fields) onto the same <see cref="SidecarAgentReply"/> shape <see cref="ChatAsync"/>'s
    /// non-streaming reply already uses (<c>elapsedMs</c> as a plain number, <c>status</c>/<c>error</c>
    /// derived from <c>HitMaxRounds</c>) — so frontend code doesn't need two different "final reply"
    /// shapes depending on which endpoint produced it.
    /// </summary>
    private static WireStreamEvent ToWireEvent(AgentStreamEvent evt) => new()
    {
        Kind = evt.Kind switch
        {
            AgentStreamEventKind.Token => "token",
            AgentStreamEventKind.ToolCallStarted => "toolCallStarted",
            AgentStreamEventKind.ToolCallResult => "toolCallResult",
            AgentStreamEventKind.Done => "done",
            AgentStreamEventKind.Error => "error",
            _ => "error"
        },
        Token = evt.Token,
        ToolName = evt.ToolName,
        ErrorMessage = evt.ErrorMessage,
        Result = evt.Result is null
            ? null
            : new SidecarAgentReply
            {
                Text = evt.Result.Text,
                ToolsUsed = evt.Result.ToolsUsed,
                Steps = evt.Steps ?? [],
                ElapsedMs = (int)evt.Result.Elapsed.TotalMilliseconds,
                Status = evt.Result.HitMaxRounds ? "failed" : "done",
                Error = false,
                Summarized = evt.Summarized,
                ContextUsagePercent = evt.ContextUsagePercent ?? 0,
            }
    };

    private sealed class WireStreamEvent
    {
        public required string Kind { get; init; }
        public string? Token { get; init; }
        public string? ToolName { get; init; }
        public SidecarAgentReply? Result { get; init; }
        public string? ErrorMessage { get; init; }
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
            estimatedTokens = agent.GetEstimatedTokens(sessionId),
            contextUsagePercent = agent.GetContextUsagePercent(sessionId),
        });
    }

    /// <summary>
    /// Runs <see cref="AgentCapabilityTester"/> against <paramref name="profile"/> (the exact
    /// in-memory field values the settings form currently shows) and returns the result.
    /// Stateless — does not persist the result. The frontend already round-trips the whole
    /// <c>UserSettings</c> blob via <c>PUT /api/config/user-settings</c> on every keystroke; it
    /// patches <c>capability</c>/<c>lastTestDiagnostic</c> into its local state from this response
    /// and saves through that same existing path rather than this endpoint owning a second,
    /// parallel persistence mechanism.
    ///
    /// Takes the profile in the request body rather than only looking it up by <paramref
    /// name="id"/> from persisted settings on purpose: that per-keystroke save is a fire-and-forget
    /// mutation the UI never awaits, so clicking "Test connection" right after editing a field could
    /// otherwise race it and silently test the previous, stale value. Falls back to the persisted
    /// lookup only if no body is sent, for any other caller of this route.
    /// </summary>
    internal static async Task<IResult> TestProfileAsync(
        string id,
        SwebKit.Core.Domain.AgentProfile? profile,
        AgentCapabilityTester tester,
        SwebKit.Core.Configuration.UserSettingsRepository settings,
        CancellationToken ct)
    {
        var target = profile ?? settings.Settings.Agent.Profiles.FirstOrDefault(p => p.Id == id);
        if (target is null)
            return Results.NotFound();

        var result = await tester.TestAsync(target, ct);
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

    /// <summary>"feature" (default) or "workspace" — workspace-intelligence Module 3's "search
    /// across my whole workspace" escalation. Orthogonal to <see cref="Mode"/>: this gates which
    /// area's tools are visible at all, not whether mutate tools are available. Anything other than
    /// exactly "workspace" (including null/omitted) is treated as "feature".</summary>
    public string? Scope { get; set; }
}
