using System.Diagnostics;
using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Core.Domain;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Decides which tools a given turn may see, and wraps the registry call the model client makes so
/// every invocation is recorded as a <c>tool_call</c>/<c>tool_result</c> step pair.
///
/// Module 5 adds two gates to which tools a given turn actually sees, both defaulting to the
/// safe/narrow option when unspecified (the pre-Module-5 caller shape — the global page today —
/// keeps working, just more conservatively than before): <c>mode</c> ("ask" keeps only
/// <see cref="ToolKind.Read"/> tools; anything else defaults to "ask" too — a typo or omitted mode
/// should never silently grant Ask &amp; do), and <see cref="AgentChatContext.FeatureArea"/> (when
/// present, keeps only tools whose <see cref="Tools.FeatureArea"/> matches — the mechanism that
/// makes a contextual panel opened from one page not see every other area's tools by default).
/// </summary>
public sealed class AgentToolCallOrchestrator
{
    internal const string AskAndDoMode = "ask_and_do";

    /// <summary>workspace-intelligence Module 3's escalation value for <c>AgentChatRequest.Scope</c>
    /// — orthogonal to <see cref="AskAndDoMode"/> (mode gates mutate tools; scope gates which area's
    /// tools are visible at all). "feature" (the default) leaves the existing per-area filter in
    /// place; "workspace" skips it for the turn.</summary>
    internal const string WorkspaceScope = "workspace";

    private readonly IAgentToolRegistry _toolRegistry;

    public AgentToolCallOrchestrator(IAgentToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
    }

    /// <summary>Anything other than exactly "ask_and_do" collapses to "ask" — see the class doc
    /// comment on why an unrecognized value never silently grants the more permissive mode.</summary>
    public static string NormalizeMode(string? mode) => mode == AskAndDoMode ? AskAndDoMode : "ask";

    /// <summary>Anything other than exactly "workspace" collapses to "feature".</summary>
    public static string NormalizeScope(string? scope) => scope == WorkspaceScope ? WorkspaceScope : "feature";

    /// <summary>Applies the three tool-visibility gates in order: capability (existing) → mode (new
    /// — "ask" keeps only Read-kind tools) → feature-area scope (new — when
    /// <paramref name="context"/> names an area, keeps only that area's tools, plus Observability's
    /// (exempt — see below); a request with no context, i.e. the global page, skips this gate
    /// entirely and keeps every area's tools, exactly like before Module 5).</summary>
    public IReadOnlyList<ToolDefinition> ResolveTools(bool hasToolCalling, string normalizedMode, AgentChatContext? context, string normalizedScope)
    {
        if (!hasToolCalling)
            return [];

        IEnumerable<ToolDefinition> tools = _toolRegistry.GetDefinitions();

        if (normalizedMode != AskAndDoMode)
            tools = tools.Where(t => t.Kind == ToolKind.Read);

        // workspace-intelligence Module 3's "search across my whole workspace" escalation: scope ==
        // "workspace" skips the per-area filter entirely for this turn (every configured area's
        // tools become visible, still subject to the capability/mode gates above), rather than
        // needing its own separate tool-visibility mechanism.
        if (normalizedScope != WorkspaceScope &&
            context?.FeatureArea is { Length: > 0 } areaName &&
            Enum.TryParse<FeatureArea>(areaName, ignoreCase: true, out var area))
        {
            // Observability is exempt from the per-area filter: it's a cross-cutting diagnostic
            // signal (traces/exceptions/metrics), not something scoped to one feature area the way
            // Redis/Storage/etc. tools are — a contextual AKS conversation should still be able to
            // pull in Application Insights context for the pod it's looking at, not just when the
            // (nonexistent) "Observability" area happens to be the active one.
            // get_screen_state is exempt for the same reason (agent-workspace-awareness D4): it
            // reads UI state, not area data — a feature-scoped panel must still be able to ask
            // what's on its own screen.
            tools = tools.Where(t =>
                t.FeatureArea == area
                || t.FeatureArea == FeatureArea.Observability
                || t.Name == GetScreenStateTool.ToolName);
        }

        return tools.ToList();
    }

    /// <summary>Wraps the raw tool registry call with step recording — same "tool_call"/"tool_result"
    /// pair shape the legacy MAUI-side <c>AgentChatService.SendAsync</c> already uses, reused rather
    /// than inventing a new trace format (workspace-intelligence Module 6).</summary>
    public Func<string, JsonElement, CancellationToken, Task<string>>? BuildStepTrackingToolExecutor(
        IReadOnlyList<ToolDefinition> tools, List<AgentChatStep> steps,
        IReadOnlyDictionary<string, string>? selection = null)
    {
        if (tools.Count == 0)
            return null;

        return async (toolName, args, toolCt) =>
        {
            var toolSw = Stopwatch.StartNew();
            var toolDef = tools.FirstOrDefault(t => t.Name == toolName);
            steps.Add(new AgentChatStep
            {
                Type = "tool_call",
                ToolName = toolName,
                Summary = toolDef?.Kind == ToolKind.Mutate
                    ? $"Preparing {toolName} (mutation)"
                    : $"Calling {toolName}",
            });

            using var executionContext = AgentExecutionContext.Push(selection);
            var result = await _toolRegistry.ExecuteAsync(toolName, args, toolCt);
            toolSw.Stop();

            steps.Add(new AgentChatStep
            {
                Type = "tool_result",
                ToolName = toolName,
                Summary = SummarizeToolResult(result),
                Elapsed = toolSw.Elapsed,
                IsFailure = IsErrorResult(result),
            });

            return result;
        };
    }

    internal static string SummarizeToolResult(string result)
    {
        if (string.IsNullOrEmpty(result))
            return "Empty result";

        return result.Length > 80 ? result[..80] + "…" : result;
    }

    /// <summary>
    /// Every tool in <c>SwebKit.Agents.Tools</c> reports a failure the same way: a top-level JSON
    /// <c>"error"</c> property in its string result (e.g. <c>{"error":"..."}</c>, sometimes alongside
    /// other context fields) — see e.g. <c>AnalyzeQueueHealthTool</c>, <c>ApiClientTools</c>,
    /// <c>Storage/*Tool.cs</c>, <c>Redis/*Tool.cs</c>. This reads that existing, already-established
    /// convention rather than inventing a new one, so a failed step (unit 7.4) is detected the same
    /// way for every tool without each one needing to change. Non-JSON or shapeless results (a plain
    /// success string) simply don't match and are treated as success, never a false positive.
    /// </summary>
    internal static bool IsErrorResult(string result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(result);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("error", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
