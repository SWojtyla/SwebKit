using System.Text.Json;
using SwebKit.Agents;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Applies a confirmed <see cref="AgentActionType.ExternalMcpCall"/> — the confirm-side half of the
/// mutation pipeline for external MCP tools (<see cref="ExternalMcpToolSource"/> registers the
/// pending action at proposal time; this executor is the only place the remote server is actually
/// invoked). Registered as one <see cref="IAgentActionExecutor"/> per the one-executor-per-area
/// convention, even though the "area" here is every external server.
/// </summary>
public sealed class ExternalMcpActionExecutor(ExternalMcpToolSource mcpToolSource) : IAgentActionExecutor
{
    public bool CanHandle(AgentActionType type) => type == AgentActionType.ExternalMcpCall;

    public async Task<AgentActionResult> ApplyAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } payload
            || !payload.TryGetProperty("serverConfigJson", out var serverEl)
            || !payload.TryGetProperty("tool", out var toolEl)
            || serverEl.GetString() is not { Length: > 0 } serverConfigJson
            || toolEl.GetString() is not { Length: > 0 } remoteName)
        {
            return new AgentActionResult { IsSuccess = false, ErrorMessage = "Malformed external-MCP action payload." };
        }

        var args = payload.TryGetProperty("args", out var argsEl) && argsEl.GetString() is { Length: > 0 } argsJson
            ? JsonDocument.Parse(argsJson).RootElement
            : JsonDocument.Parse("{}").RootElement;

        string result;
        try
        {
            result = await mcpToolSource.ExecuteConfirmedAsync(serverConfigJson, remoteName, args, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AgentActionResult { IsSuccess = false, ErrorMessage = ex.Message };
        }

        var isError = AgentToolCallOrchestrator.IsErrorResult(result);
        return new AgentActionResult
        {
            IsSuccess = !isError,
            ErrorMessage = isError ? result : null,
            ResultSummary = isError ? null : Summarize(result),
        };
    }

    private static string Summarize(string json) =>
        json.Length > 500 ? json[..500] + "…" : json;
}
