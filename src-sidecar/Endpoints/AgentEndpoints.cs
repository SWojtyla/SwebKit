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
}

public sealed class AgentChatRequest
{
    public string Message { get; set; } = string.Empty;
}
