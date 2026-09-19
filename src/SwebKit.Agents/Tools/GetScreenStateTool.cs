using System.Text.Json;

namespace SwebKit.Agents.Tools;

/// <summary>Returns what the user is currently looking at — the bounded snapshot the React app
/// last published to <see cref="ScreenStateStore"/> (agent-workspace-awareness Module 1). This is
/// the pull-model counterpart to the <c>## Current focus</c> prompt section: the prompt already
/// says *where* the user is; this tool answers *what they see*, with data the UI already fetched
/// — so the model doesn't re-fetch rows that are literally on screen.
///
/// Declared <see cref="FeatureArea.Workspace"/> but explicitly exempted from the per-area filter
/// in <c>AgentToolCallOrchestrator.ResolveTools</c> (alongside Observability): screen state is UI
/// state, not area data — an AKS-scoped panel must be able to ask what's on the AKS screen.
/// Reaches ACP agents through the MCP bridge's per-request allowlist like any other tool.</summary>
public sealed class GetScreenStateTool : IAgentTool
{
    public const string ToolName = "get_screen_state";

    private static readonly JsonElement Schema = AgentToolSchema.Parse("""
        { "type": "object", "properties": {}, "additionalProperties": false }
        """);

    private readonly ScreenStateStore _store;

    public GetScreenStateTool(ScreenStateStore store) => _store = store;

    public string Name => ToolName;

    public string Description =>
        "Returns what the user currently has on their screen in the app — the visible page, " +
        "their selection, and a bounded slice of the data the UI already fetched (pods listed, " +
        "message/blob/key being inspected, active filters). Call this when the user's question " +
        "refers to what they can see ('this pod', 'the error on screen', 'why is this failing') " +
        "instead of re-fetching the same data with another tool.";

    public JsonElement ParametersSchema => Schema;

    public FeatureArea FeatureArea => FeatureArea.Workspace;

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var current = _store.Current;
        if (current is null)
        {
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                available = false,
                reason = "No screen state has been published recently — the user's screen may be " +
                    "idle or the window is closed. Fall back to the usual data tools.",
            }));
        }

        var ageSeconds = Math.Max(0, (int)(DateTimeOffset.UtcNow - current.CapturedAt).TotalSeconds);
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            available = true,
            route = current.Route,
            featureArea = current.FeatureArea,
            capturedAt = current.CapturedAt,
            ageSeconds,
            snapshot = current.Snapshot,
        }));
    }
}
