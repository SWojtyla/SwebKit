using System.Collections.Concurrent;
using SwebKit.Agents;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Owns the conversation history of every agent chat session and its lifetime.
///
/// Holds one <see cref="AgentConversationSession"/> per <c>sessionId</c> (a per-panel id the frontend
/// generates once per mounted contextual assistant, see <c>ai-augmented-app</c> technical-plan.md
/// Module 2), so a conversation opened from an AKS pod panel doesn't share history with one opened
/// from a Redis key panel, or with the global <c>/agent</c> page. A null/omitted <c>sessionId</c>
/// maps to a single fixed key (<see cref="GlobalSessionKey"/>) — this preserves the pre-Module-2
/// behavior of the global page exactly (one shared conversation, never evicted), which is why that
/// key is exempt from idle eviction while every other session isn't.
/// </summary>
public sealed class AgentSessionStore
{
    internal const string GlobalSessionKey = "__global__";

    private static readonly TimeSpan IdleSessionTimeout = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, AgentConversationSession> _sessions = new();
    private readonly int _maxHistory = 20;

    public static string Key(string? sessionId) =>
        string.IsNullOrWhiteSpace(sessionId) ? GlobalSessionKey : sessionId;

    /// <summary>Returns the session for <paramref name="key"/> (already normalized through
    /// <see cref="Key"/>), creating it on first use.</summary>
    public AgentConversationSession GetOrCreate(string key) =>
        _sessions.GetOrAdd(key, _ => new AgentConversationSession());

    public bool TryGet(string? sessionId, out AgentConversationSession session) =>
        _sessions.TryGetValue(Key(sessionId), out session!);

    /// <summary>Creates a session under the <em>raw</em> (un-normalized) id, or returns null if one
    /// already exists — the no-op safeguard behind
    /// <see cref="SidecarAgentChatService.SeedProactiveInsightSession"/>.</summary>
    public AgentConversationSession? CreateIfAbsent(string sessionId)
    {
        if (_sessions.ContainsKey(sessionId))
            return null;

        return _sessions.GetOrAdd(sessionId, _ => new AgentConversationSession());
    }

    public int GetHistoryCount(string? sessionId) =>
        TryGet(sessionId, out var session) ? session.History.Count : 0;

    /// <summary>
    /// Rough token estimate for a session's history (~4 characters per token — the standard
    /// coarse heuristic for English text, not real tokenization) so the UI can show the user
    /// something to watch as a conversation grows, without needing a per-model tokenizer or a
    /// user-configured context-window size (both of which belong to the model/provider, not this
    /// app — see the "AI Agent" settings simplification this accompanies). Deliberately excludes
    /// the system prompt, which is rebuilt fresh per turn rather than accumulating in history.
    /// </summary>
    public int GetEstimatedTokens(string? sessionId)
    {
        if (!TryGet(sessionId, out var session))
            return 0;

        var totalChars = session.History.Sum(m => m.Content?.Length ?? 0);
        return (int)Math.Ceiling(totalChars / 4.0);
    }

    public void ClearHistory(string? sessionId = null)
    {
        if (TryGet(sessionId, out var session))
            while (session.History.TryDequeue(out _)) { }
    }

    /// <summary>Records a message and applies the fixed history cap in one step — the two always
    /// happen together on every turn-taking path.</summary>
    public void Append(AgentConversationSession session, AgentMessage message)
    {
        session.History.Enqueue(message);
        Trim(session);
    }

    public void Trim(AgentConversationSession session)
    {
        while (session.History.Count > _maxHistory && session.History.TryDequeue(out _)) { }
    }

    /// <summary>Lazily sweeps idle contextual sessions on each call rather than running a background
    /// timer — cheap for the handful of concurrent sessions a single-user desktop app has, and
    /// avoids a real-time-based background loop that would need faking in tests. The global session
    /// is exempt (see the class doc comment): it's meant to persist for the app's whole lifetime,
    /// matching its pre-Module-2 behavior exactly.</summary>
    public void EvictIdleSessions()
    {
        var cutoff = DateTimeOffset.UtcNow - IdleSessionTimeout;
        foreach (var (key, session) in _sessions)
        {
            if (key != GlobalSessionKey && session.LastActivity < cutoff)
                _sessions.TryRemove(key, out _);
        }
    }
}

/// <summary>One conversation's mutable state: its transcript plus the context-budget measurements
/// taken on its most recent turn.</summary>
public sealed class AgentConversationSession
{
    public ConcurrentQueue<AgentMessage> History { get; } = new();
    public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Estimated token size of the most recently sent request for this session (post
    /// rolling-summarization trim, if one happened) — workspace-intelligence Module 5/6.</summary>
    public int LastRequestEstimatedTokens { get; set; }

    /// <summary>The effective context window (profile's declared value or the conservative
    /// default) that <see cref="LastRequestEstimatedTokens"/> was measured against.</summary>
    public int LastContextWindowTokens { get; set; }
}
