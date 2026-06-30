using System.Diagnostics;
using System.Text.Json;

namespace SwebKit.Agents;

/// <summary>
/// Represents a single assistant reply returned to the UI after a full Mistral round-trip.
/// </summary>
public sealed class AgentChatReply
{
    /// <summary>The text content of the assistant's final message.</summary>
    public required string Text { get; init; }

    /// <summary>Names of the tools that were invoked during this request (may be empty).</summary>
    public IReadOnlyList<string> ToolsUsed { get; init; } = [];

    /// <summary>Total wall-clock time for the request (including all tool-call round-trips).</summary>
    public TimeSpan Elapsed { get; init; }
}

/// <summary>
/// Provides a high-level chat interface with conversation history management on top of
/// <see cref="IMistralClient"/>.  Maintains a single <see cref="ConversationSession"/>
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
