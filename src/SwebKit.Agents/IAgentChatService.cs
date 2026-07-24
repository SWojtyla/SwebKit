using System.Diagnostics;
using System.Text.Json;
using SwebKit.Agents.Tools;

namespace SwebKit.Agents;

/// <summary>
/// A single step in the agent's execution, exposed to the UI for progress display.
/// </summary>
public sealed class AgentChatStep
{
    /// <summary>Step type: "tool_call", "tool_result", "thinking".</summary>
    public required string Type { get; init; }

    /// <summary>Tool name if this is a tool step.</summary>
    public string? ToolName { get; init; }

    /// <summary>Brief summary of what happened (non-sensitive).</summary>
    public string? Summary { get; init; }

    /// <summary>Elapsed time for this step.</summary>
    public TimeSpan Elapsed { get; init; }
}

/// <summary>
/// Summary of a pending action awaiting user confirmation.
/// </summary>
public sealed class AgentActionSummary
{
    public required string Id { get; init; }
    public required string Action { get; init; }
    public required string Target { get; init; }
    public ToolRisk Risk { get; init; }
    public string? Preview { get; init; }
}

/// <summary>
/// Represents a single assistant reply returned to the UI after a full LLM round-trip.
/// </summary>
public sealed class AgentChatReply
{
    /// <summary>The text content of the assistant's final message.</summary>
    public required string Text { get; init; }

    /// <summary>Names of the tools that were invoked during this request (may be empty).</summary>
    public IReadOnlyList<string> ToolsUsed { get; init; } = [];

    /// <summary>Detailed steps for UI progress display.</summary>
    public IReadOnlyList<AgentChatStep> Steps { get; init; } = [];

    /// <summary>Pending actions awaiting user confirmation (empty if none).</summary>
    public IReadOnlyList<AgentActionSummary> PendingActions { get; init; } = [];

    /// <summary>Total wall-clock time for the request (including all tool-call round-trips).</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Agent status for UI display.</summary>
    public AgentStatus Status { get; init; } = AgentStatus.Done;
}

/// <summary>
/// Status of the agent for UI display.
/// </summary>
public enum AgentStatus
{
    Thinking,
    ReadingContext,
    PreparingChange,
    AwaitingConfirmation,
    Applying,
    Done,
    Failed,
}

/// <summary>
/// Provides a high-level chat interface with conversation history management on top of
/// <see cref="IAgentModelClient"/>.  Maintains a single <see cref="ConversationSession"/>
/// that persists for the lifetime of the service.
/// </summary>
public interface IAgentChatService
{
    /// <summary>
    /// Sends <paramref name="userMessage"/> to the agent and returns the assistant reply.
    /// History is updated in-place so subsequent calls retain context.
    /// </summary>
    Task<AgentChatReply> SendAsync(string userMessage, CancellationToken ct = default);

    /// <summary>Resets the conversation history to an empty state.</summary>
    void ClearHistory();

    /// <summary>Number of messages currently in history.</summary>
    int HistoryMessageCount { get; }

    /// <summary>
    /// <see langword="true"/> when history has reached the configured warning threshold
    /// and the UI should display a "history almost full" notice.
    /// </summary>
    bool IsNearHistoryLimit { get; }
}
