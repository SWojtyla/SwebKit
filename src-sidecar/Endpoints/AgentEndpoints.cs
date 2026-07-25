using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Endpoints;

public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this WebApplication app)
    {
        // ── Send message ────────────────────────────────────────────────────────

        app.MapPost("/api/agent/chat", async (
            SidecarAgentChatService agent,
            AgentChatRequest req,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Message))
                return Results.BadRequest("Message is required");

            var reply = await agent.SendAsync(req.Message, ct);
            return Results.Ok(reply);
        });

        // ── Clear history ───────────────────────────────────────────────────────

        app.MapPost("/api/agent/clear", (SidecarAgentChatService agent) =>
        {
            agent.ClearHistory();
            return Results.Ok(new { cleared = true });
        });

        // ── Get status ──────────────────────────────────────────────────────────

        app.MapGet("/api/agent/status", (SidecarAgentChatService agent) =>
        {
            return Results.Ok(new
            {
                historyCount = agent.HistoryCount,
            });
        });
    }
}

public sealed class AgentChatRequest
{
    public string Message { get; set; } = string.Empty;
}
