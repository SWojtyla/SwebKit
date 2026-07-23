using System.Text.Json;

namespace SwebKit.Agents;

// ── Request DTOs ──

/// <summary>
/// A single message in the conversation, typed for reliable serialization and multi-provider support.
/// </summary>
public sealed class AgentMessage
{
    /// <summary>Role: "system", "user", "assistant", or "tool".</summary>
    public required string Role { get; init; }

    /// <summary>Text content (may be null for assistant messages that only contain tool_calls).</summary>
    public string? Content { get; init; }

    /// <summary>
    /// Tool calls issued by the assistant (only for role="assistant" with tool_calls).
    /// Null when not applicable.
    /// </summary>
    public IReadOnlyList<AgentToolCall>? ToolCalls { get; init; }

    /// <summary>
    /// Tool call ID this message responds to (only for role="tool").
    /// </summary>
    public string? ToolCallId { get; init; }

    /// <summary>Serializes to the OpenAI-compatible wire format.</summary>
    public Dictionary<string, object> ToWireFormat()
    {
        var dict = new Dictionary<string, object> { ["role"] = Role };
        if (Content is not null)
            dict["content"] = Content;
        if (ToolCalls is not null && ToolCalls.Count > 0)
        {
            dict["tool_calls"] = ToolCalls.Select(tc => new
            {
                id = tc.Id,
                type = "function",
                function = new { name = tc.Name, arguments = tc.ArgumentsJson }
            }).ToArray();
        }
        if (ToolCallId is not null)
            dict["tool_call_id"] = ToolCallId;
        return dict;
    }
}

/// <summary>
/// A tool call requested by the model.
/// </summary>
public sealed class AgentToolCall
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Raw JSON arguments string from the model.</summary>
    public required string ArgumentsJson { get; init; }
}

/// <summary>
/// Request to the LLM model client.
/// </summary>
public sealed class AgentModelRequest
{
    public required string SystemPrompt { get; init; }
    public required string UserMessage { get; init; }
    public IReadOnlyList<ToolDefinition> Tools { get; init; } = [];
    public IReadOnlyList<AgentMessage> History { get; init; } = [];
    public double Temperature { get; init; } = 0.7;
    public int MaxTokens { get; init; } = 2048;
    public int MaxToolRounds { get; init; } = 5;
}

// ── Response DTOs ──

/// <summary>
/// Why the model stopped generating.
/// </summary>
public enum AgentFinishReason
{
    Stop,
    ToolCalls,
    Length,
    ContentFilter,
    Unknown,
}

/// <summary>
/// Response from the LLM model client after a single completion call (not the full agentic loop).
/// </summary>
public sealed class AgentModelResponse
{
    /// <summary>Finish reason from the API.</summary>
    public AgentFinishReason FinishReason { get; init; } = AgentFinishReason.Unknown;

    /// <summary>Text content from the assistant (may be null if only tool_calls).</summary>
    public string? Content { get; init; }

    /// <summary>Tool calls requested by the model (null if none).</summary>
    public IReadOnlyList<AgentToolCall>? ToolCalls { get; init; }

    /// <summary>The assistant message to append to history (includes tool_calls if any).</summary>
    public AgentMessage AssistantMessage { get; init; } = null!;
}

/// <summary>
/// Final result of the agentic loop.
/// </summary>
public sealed class AgentChatResult
{
    public required string Text { get; init; }
    public IReadOnlyList<string> ToolsUsed { get; init; } = [];
    public TimeSpan Elapsed { get; init; }
    public bool HitMaxRounds { get; init; }
}

/// <summary>
/// Provider-agnostic LLM client interface. Replaces <c>IMistralClient</c>.
/// </summary>
public interface IAgentModelClient
{
    /// <summary>
    /// Sends a user message and runs the full agentic loop: if the model requests
    /// tool calls, <paramref name="toolExecutor"/> is invoked for each, and the
    /// results are sent back before returning the final text response.
    /// </summary>
    Task<AgentChatResult> ChatAsync(
        AgentModelRequest request,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        CancellationToken ct);

    /// <summary>
    /// Sends a single completion request (no agentic loop). Used for capability testing.
    /// </summary>
    Task<AgentModelResponse> CompleteAsync(
        AgentModelRequest request,
        CancellationToken ct);
}
