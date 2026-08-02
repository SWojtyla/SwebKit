using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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
    }

    internal static async Task<IResult> ChatAsync(
        SidecarAgentChatService agent,
        AgentChatRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return Results.BadRequest("Message is required");

        var reply = await agent.SendAsync(req.Message, ct);
        return Results.Ok(reply);
    }

    internal static Ok<object> ClearHistory(SidecarAgentChatService agent)
    {
        agent.ClearHistory();
        return TypedResults.Ok<object>(new { cleared = true });
    }

    internal static Ok<object> GetStatus(SidecarAgentChatService agent)
    {
        return TypedResults.Ok<object>(new
        {
            historyCount = agent.HistoryCount,
        });
    }
}

public sealed class AgentChatRequest
{
    public string Message { get; set; } = string.Empty;
}
