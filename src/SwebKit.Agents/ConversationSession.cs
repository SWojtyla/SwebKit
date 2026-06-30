namespace SwebKit.Agents;

/// <summary>
/// Holds the conversation message history for a single chat session with the Mistral agent.
/// Enforces a configurable maximum size by trimming the oldest user/assistant exchange pairs.
/// </summary>
/// <remarks>
/// The <see cref="Messages"/> list stores raw Mistral message objects (anonymous records or
/// <c>Dictionary&lt;string, object&gt;</c>) and is passed directly to
/// <see cref="IMistralClient.ChatAsync"/>.
/// </remarks>
public sealed class ConversationSession
{
    private readonly List<object> _messages = [];
    private int _maxMessages;

    public ConversationSession(int maxMessages = 20)
    {
        _maxMessages = maxMessages > 0 ? maxMessages : 20;
    }

    /// <summary>Number of messages currently in history.</summary>
    public int Count => _messages.Count;

    /// <summary>Configured maximum number of messages before trimming occurs.</summary>
    public int MaxMessages
    {
        get => _maxMessages;
        set => _maxMessages = value > 0 ? value : 20;
    }

    /// <summary>
    /// Returns <see langword="true"/> when history has reached 75 % or more of the limit,
    /// giving the UI an opportunity to display a warning.
    /// </summary>
    public bool IsNearLimit => _maxMessages > 0 && _messages.Count >= (int)(_maxMessages * 0.75);

    /// <summary>Read-only view of the current history, suitable for passing to ChatAsync.</summary>
    public IReadOnlyList<object> Messages => _messages;

    /// <summary>
    /// Appends a message to history, then trims the oldest user/assistant exchange pair
    /// when the count exceeds <see cref="MaxMessages"/>.
    /// </summary>
    public void Add(object message)
    {
        _messages.Add(message);
        TrimIfNeeded();
    }

    /// <summary>Removes all messages from history.</summary>
    public void Clear() => _messages.Clear();

    // Trims the oldest complete exchange (user + at least one assistant/tool reply)
    // until the count is within the limit.
    private void TrimIfNeeded()
    {
        while (_messages.Count > _maxMessages && _messages.Count >= 2)
        {
            // Remove the two oldest messages (index 0 and what was index 1).
            _messages.RemoveAt(0);
            _messages.RemoveAt(0);
        }
    }
}
