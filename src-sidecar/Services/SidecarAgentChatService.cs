using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Sidecar-specific agent chat service that wraps <see cref="IAgentModelClient"/>
/// with conversation history and context built from <see cref="ProfileRepository"/>.
///
/// Holds one <see cref="ConversationSession"/> per <c>sessionId</c> (a per-panel id the frontend
/// generates once per mounted contextual assistant, see <c>ai-augmented-app</c> technical-plan.md
/// Module 2), so a conversation opened from an AKS pod panel doesn't share history with one opened
/// from a Redis key panel, or with the global <c>/agent</c> page. A null/omitted <c>sessionId</c>
/// maps to a single fixed key (<see cref="GlobalSessionKey"/>) — this preserves the pre-Module-2
/// behavior of the global page exactly (one shared conversation, never evicted), which is why that
/// key is exempt from idle eviction while every other session isn't.
///
/// Module 5 adds two more gates to which tools a given turn actually sees, both defaulting to the
/// safe/narrow option when unspecified (the pre-Module-5 caller shape — the global page today —
/// keeps working, just more conservatively than before): <c>mode</c> ("ask" keeps only
/// <see cref="ToolKind.Read"/> tools; anything else defaults to "ask" too — a typo or omitted mode
/// should never silently grant Ask & do), and <see cref="AgentChatContext.FeatureArea"/> (when
/// present, keeps only tools whose <see cref="Tools.FeatureArea"/> matches — the mechanism that
/// makes a contextual panel opened from one page not see every other area's tools by default).
/// </summary>
public sealed class SidecarAgentChatService
{
    private const string GlobalSessionKey = "__global__";
    private const string AskAndDoMode = "ask_and_do";
    private static readonly TimeSpan IdleSessionTimeout = TimeSpan.FromMinutes(30);

    private readonly IAgentModelClient _modelClient;
    private readonly IAgentToolRegistry _toolRegistry;
    private readonly ProfileRepository _profiles;
    private readonly UserSettingsRepository _settings;
    private readonly DemoModeService _demo;
    private readonly ConcurrentDictionary<string, ConversationSession> _sessions = new();
    private readonly int _maxHistory = 20;

    /// <summary>History count for the global <c>/agent</c> page's session. Kept for existing call
    /// sites (<see cref="AgentEndpoints.GetStatus"/>); prefer <see cref="GetHistoryCount"/> for new
    /// per-session call sites.</summary>
    public int HistoryCount => GetHistoryCount(null);

    public SidecarAgentChatService(
        IAgentModelClient modelClient,
        IAgentToolRegistry toolRegistry,
        ProfileRepository profiles,
        UserSettingsRepository settings,
        DemoModeService demo)
    {
        _modelClient = modelClient;
        _toolRegistry = toolRegistry;
        _profiles = profiles;
        _settings = settings;
        _demo = demo;
    }

    public int GetHistoryCount(string? sessionId) =>
        _sessions.TryGetValue(Key(sessionId), out var session) ? session.History.Count : 0;

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
        if (!_sessions.TryGetValue(Key(sessionId), out var session))
            return 0;

        var totalChars = session.History.Sum(m => m.Content?.Length ?? 0);
        return (int)Math.Ceiling(totalChars / 4.0);
    }

    public void ClearHistory(string? sessionId = null)
    {
        if (_sessions.TryGetValue(Key(sessionId), out var session))
            while (session.History.TryDequeue(out _)) { }
    }

    /// <summary>Overload preserving the pre-Module-5 call shape: no session, no context, and the
    /// safe "ask" mode (not "ask_and_do" — see the class doc comment on why unspecified always
    /// means the narrower option).</summary>
    public Task<SidecarAgentReply> SendAsync(string userMessage, CancellationToken ct = default) =>
        SendAsync(null, userMessage, context: null, mode: null, ct);

    public async Task<SidecarAgentReply> SendAsync(
        string? sessionId,
        string userMessage,
        AgentChatContext? context = null,
        string? mode = null,
        CancellationToken ct = default)
    {
        EvictIdleSessions();
        var key = Key(sessionId);
        var session = _sessions.GetOrAdd(key, _ => new ConversationSession());
        session.LastActivity = DateTimeOffset.UtcNow;

        var normalizedMode = mode == AskAndDoMode ? AskAndDoMode : "ask";

        var sw = Stopwatch.StartNew();
        var profile = _settings.Settings.Agent.GetActiveProfile();
        var hasToolCalling = (profile?.Capability ?? AgentCapability.Unknown) >= AgentCapability.ToolCalling;
        var systemPrompt = BuildSystemPrompt(context, normalizedMode, hasToolCalling);

        // Record user message
        session.History.Enqueue(new AgentMessage { Role = "user", Content = userMessage });
        TrimHistory(session);

        var historyList = session.History.ToList();
        // Remove the last (user message) from history passed to the model since it's passed separately
        if (historyList.Count > 0)
            historyList.RemoveAt(historyList.Count - 1);

        var tools = ResolveTools(hasToolCalling, normalizedMode, context);

        var request = new AgentModelRequest
        {
            SystemPrompt = systemPrompt,
            UserMessage = userMessage,
            Tools = tools,
            History = historyList,
        };

        try
        {
            var result = await _modelClient.ChatAsync(
                request,
                tools.Count > 0 ? (toolName, args, toolCt) => _toolRegistry.ExecuteAsync(toolName, args, toolCt) : null,
                ct);
            session.History.Enqueue(new AgentMessage { Role = "assistant", Content = result.Text });
            TrimHistory(session);

            sw.Stop();
            return new SidecarAgentReply
            {
                Text = result.Text,
                ToolsUsed = result.ToolsUsed,
                ElapsedMs = (int)sw.Elapsed.TotalMilliseconds,
                Status = result.HitMaxRounds ? "failed" : "done",
                Error = false,
            };
        }
        catch (Exception ex)
        {
            session.History.Enqueue(new AgentMessage { Role = "assistant", Content = $"Error: {ex.Message}" });
            TrimHistory(session);

            sw.Stop();
            return new SidecarAgentReply
            {
                Text = $"Error: {ex.Message}",
                ElapsedMs = (int)sw.Elapsed.TotalMilliseconds,
                Status = "failed",
                Error = true,
            };
        }
    }

    /// <summary>Streaming counterpart to <see cref="SendAsync"/> — same session/tool/prompt setup,
    /// but forwards <see cref="IAgentModelClient.ChatStreamAsync"/>'s events as they arrive instead
    /// of waiting for the final result. History is only updated once, from the terminal
    /// <see cref="AgentStreamEventKind.Done"/>/<see cref="AgentStreamEventKind.Error"/> event — never
    /// from intermediate token events — so a client that disconnects mid-stream doesn't leave a
    /// partial assistant message in history.</summary>
    public async IAsyncEnumerable<AgentStreamEvent> SendStreamAsync(
        string? sessionId,
        string userMessage,
        AgentChatContext? context = null,
        string? mode = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        EvictIdleSessions();
        var key = Key(sessionId);
        var session = _sessions.GetOrAdd(key, _ => new ConversationSession());
        session.LastActivity = DateTimeOffset.UtcNow;

        var normalizedMode = mode == AskAndDoMode ? AskAndDoMode : "ask";

        var profile = _settings.Settings.Agent.GetActiveProfile();
        var hasToolCalling = (profile?.Capability ?? AgentCapability.Unknown) >= AgentCapability.ToolCalling;
        var systemPrompt = BuildSystemPrompt(context, normalizedMode, hasToolCalling);

        session.History.Enqueue(new AgentMessage { Role = "user", Content = userMessage });
        TrimHistory(session);

        var historyList = session.History.ToList();
        if (historyList.Count > 0)
            historyList.RemoveAt(historyList.Count - 1);

        var tools = ResolveTools(hasToolCalling, normalizedMode, context);

        var request = new AgentModelRequest
        {
            SystemPrompt = systemPrompt,
            UserMessage = userMessage,
            Tools = tools,
            History = historyList,
        };

        var stream = _modelClient.ChatStreamAsync(
            request,
            tools.Count > 0 ? (toolName, args, toolCt) => _toolRegistry.ExecuteAsync(toolName, args, toolCt) : null,
            ct);
        var enumerator = stream.GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                AgentStreamEvent? current = null;
                var hasNext = false;
                Exception? caught = null;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                    if (hasNext)
                        current = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    caught = ex;
                }

                if (caught is not null)
                {
                    session.History.Enqueue(new AgentMessage { Role = "assistant", Content = $"Error: {caught.Message}" });
                    TrimHistory(session);
                    yield return new AgentStreamEvent { Kind = AgentStreamEventKind.Error, ErrorMessage = caught.Message };
                    yield break;
                }

                if (!hasNext)
                    yield break;

                if (current!.Kind == AgentStreamEventKind.Done && current.Result is not null)
                {
                    session.History.Enqueue(new AgentMessage { Role = "assistant", Content = current.Result.Text });
                    TrimHistory(session);
                }
                else if (current.Kind == AgentStreamEventKind.Error)
                {
                    session.History.Enqueue(new AgentMessage { Role = "assistant", Content = $"Error: {current.ErrorMessage}" });
                    TrimHistory(session);
                }

                yield return current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    /// <summary>Applies the three tool-visibility gates in order: capability (existing) → mode (new
    /// — "ask" keeps only Read-kind tools) → feature-area scope (new — when
    /// <paramref name="context"/> names an area, keeps only that area's tools; a request with no
    /// context, i.e. the global page, skips this gate entirely and keeps every area's tools, exactly
    /// like before Module 5).</summary>
    private IReadOnlyList<ToolDefinition> ResolveTools(bool hasToolCalling, string normalizedMode, AgentChatContext? context)
    {
        if (!hasToolCalling)
            return [];

        IEnumerable<ToolDefinition> tools = _toolRegistry.GetDefinitions();

        if (normalizedMode != AskAndDoMode)
            tools = tools.Where(t => t.Kind == ToolKind.Read);

        if (context?.FeatureArea is { Length: > 0 } areaName &&
            Enum.TryParse<FeatureArea>(areaName, ignoreCase: true, out var area))
        {
            tools = tools.Where(t => t.FeatureArea == area);
        }

        return tools.ToList();
    }

    private static string Key(string? sessionId) =>
        string.IsNullOrWhiteSpace(sessionId) ? GlobalSessionKey : sessionId;

    private void TrimHistory(ConversationSession session)
    {
        while (session.History.Count > _maxHistory && session.History.TryDequeue(out _)) { }
    }

    /// <summary>Lazily sweeps idle contextual sessions on each call rather than running a background
    /// timer — cheap for the handful of concurrent sessions a single-user desktop app has, and
    /// avoids a real-time-based background loop that would need faking in tests. The global session
    /// is exempt (see the class doc comment): it's meant to persist for the app's whole lifetime,
    /// matching its pre-Module-2 behavior exactly.</summary>
    private void EvictIdleSessions()
    {
        var cutoff = DateTimeOffset.UtcNow - IdleSessionTimeout;
        foreach (var (key, session) in _sessions)
        {
            if (key != GlobalSessionKey && session.LastActivity < cutoff)
                _sessions.TryRemove(key, out _);
        }
    }

    private string BuildSystemPrompt(AgentChatContext? context, string normalizedMode, bool hasToolCalling)
    {
        var data = _profiles.GetProfileData();
        var config = data.Config;
        var contextParts = new List<string>();

        // Kubernetes
        if (config.AksConfig is not null)
            contextParts.Add($"Kubernetes: context={config.AksConfig.KubeconfigContext ?? "default"}");
        else
            contextParts.Add("Kubernetes: (not configured)");

        // Service Bus
        var sbNames = data.ServiceBusNamespaces;
        if (sbNames.Count > 0)
            contextParts.Add($"Service Bus: {string.Join(", ", sbNames.Select(n => n.Alias))}");

        // Redis
        if (config.RedisConfig is not null && config.RedisConfig.Caches.Count > 0)
            contextParts.Add($"Redis: {config.RedisConfig.Caches.Count} cache(s)");

        // Storage
        if (config.StorageAccounts.Count > 0)
            contextParts.Add($"Storage: {config.StorageAccounts.Count} account(s)");

        // DevOps
        if (config.DevOpsConfig is not null && !string.IsNullOrWhiteSpace(config.DevOpsConfig.Organization))
            contextParts.Add($"DevOps: {config.DevOpsConfig.Organization}");

        // Observability
        if (config.ObservabilityConfig is not null && !string.IsNullOrWhiteSpace(config.ObservabilityConfig.SelectedResourceId))
            contextParts.Add($"Observability: {config.ObservabilityConfig.SelectedResourceName ?? config.ObservabilityConfig.SelectedResourceId}");

        if (_demo.IsDemoMode)
            contextParts.Add("Demo mode: enabled (using synthetic data)");

        var workspaceContext = contextParts.Count == 0
            ? "No workspace services configured."
            : string.Join(" | ", contextParts);

        var currentFocus = BuildCurrentFocusSection(context);

        var toolPolicy = BuildToolPolicySection(hasToolCalling, normalizedMode);

        return $"""
            You are SwebKit Assistant, an AI copilot embedded in SwebKit — a DevOps operations desktop
            application for platform engineers. You help users diagnose and understand their Kubernetes
            clusters, Azure DevOps pipelines, Redis instances, Azure Service Bus queues, Storage accounts,
            and observability data.
            {currentFocus}
            ## Current workspace context
            {workspaceContext}

            ## Response format
            - Be concise and technical. Prefer bullet points and tables over prose.
            - If you are unsure, say so rather than guessing.

            {toolPolicy}

            ## Limits
            - No Observability tools are available in the sidecar mode yet.
            - No Git operations.
            """;
    }

    /// <summary>Additive detail ahead of the general workspace summary, describing exactly what the
    /// user has open right now (e.g. "the AKS pod panel for pod api-7c9f in namespace prod") — empty
    /// for the global page, which passes no context. Never replaces the coarse workspace summary
    /// below it.</summary>
    private static string BuildCurrentFocusSection(AgentChatContext? context)
    {
        if (context is null || string.IsNullOrEmpty(context.FeatureArea))
            return "";

        var lines = new List<string> { $"Area: {context.FeatureArea}" };
        if (context.Selection is { Count: > 0 })
            lines.AddRange(context.Selection.Select(kv => $"{kv.Key}: {kv.Value}"));

        return $"""

            ## Current focus
            {string.Join("\n", lines)}

            """;
    }

    private static string BuildToolPolicySection(bool hasToolCalling, string normalizedMode)
    {
        if (!hasToolCalling)
        {
            return """
                ## Tool policy
                - Tool calling is not available with the current model. Answer based on context only.
                - If the user needs live data, suggest enabling a model that supports tool calling.
                """;
        }

        if (normalizedMode == AskAndDoMode)
        {
            return """
                ## Tool policy (Ask & do mode)
                - Use tools to fetch live data, and propose changes with the Propose*/Prepare* tools when asked.
                - Every mutating tool only proposes a pending action — it never changes anything by itself.
                  The user must explicitly confirm before anything is applied.
                - If a tool returns an error, explain what it means and suggest a resolution.
                - Do not expose internal JSON schemas or tool names in your replies.
                """;
        }

        return """
            ## Tool policy (Ask mode)
            - Use tools to fetch live data, but you have no mutating tools available in this mode —
              nothing you do can change the user's cluster, queues, caches, storage, or collections,
              no matter what is asked. If the user wants to change something, tell them to switch to
              Ask & do mode.
            - If a tool returns an error, explain what it means and suggest a resolution.
            - Do not expose internal JSON schemas or tool names in your replies.
            """;
    }

    private sealed class ConversationSession
    {
        public ConcurrentQueue<AgentMessage> History { get; } = new();
        public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.UtcNow;
    }
}

/// <summary>What the user currently has open, passed by a contextual assistant panel so the model
/// can be told exactly what's on screen and so tool visibility can be scoped to that one area (see
/// <c>SidecarAgentChatService.ResolveTools</c>). The global <c>/agent</c> page passes null.</summary>
public sealed class AgentChatContext
{
    /// <summary>Name of a <see cref="FeatureArea"/> enum member (e.g. "Aks", "Redis") — a string on
    /// the wire since the frontend has no reason to import the C# enum; parsed server-side.</summary>
    public string? FeatureArea { get; set; }

    /// <summary>Free-form key/value pairs describing the current selection (e.g. namespace/pod,
    /// cache/key, requestId) — whatever the page already tracks, passed through unmodified.</summary>
    public Dictionary<string, string>? Selection { get; set; }
}

public sealed class SidecarAgentReply
{
    public required string Text { get; init; }
    public IReadOnlyList<string> ToolsUsed { get; init; } = [];
    public int ElapsedMs { get; init; }
    public string Status { get; init; } = "done";
    public bool Error { get; init; }
}
