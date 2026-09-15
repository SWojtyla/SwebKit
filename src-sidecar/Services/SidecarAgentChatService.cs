using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Sidecar-specific agent chat service that wraps <see cref="IAgentModelClient"/>
/// with conversation history and context built from <see cref="ProfileRepository"/>.
///
/// A thin coordinator over four collaborators, each owning one of the concerns this class used to
/// mix together: <see cref="AgentSessionStore"/> (per-session history, trimming, idle eviction),
/// <see cref="AgentSystemPromptBuilder"/> (the per-turn system prompt),
/// <see cref="AgentToolCallOrchestrator"/> (tool visibility gates + <c>tool_call</c>/<c>tool_result</c>
/// step recording), and <see cref="AgentContextBudgetPlanner"/> (token budgeting and rolling
/// summarization). What remains here is the turn-taking itself — the shared setup, and the two
/// request paths (<see cref="SendAsync(string?, string, AgentChatContext?, string?, string?, CancellationToken)"/>
/// and <see cref="SendStreamAsync"/>) that differ only in how the model's answer arrives.
/// </summary>
public sealed class SidecarAgentChatService
{
    private readonly IAgentModelClient _modelClient;
    private readonly UserSettingsRepository _settings;
    private readonly AgentSessionStore _sessions;
    private readonly AgentSystemPromptBuilder _promptBuilder;
    private readonly AgentToolCallOrchestrator _toolOrchestrator;
    private readonly AgentContextBudgetPlanner _budgetPlanner;
    private readonly Acp.AcpAgentHost? _acpHost;

    /// <summary>History count for the global <c>/agent</c> page's session. Kept for existing call
    /// sites (<see cref="AgentEndpoints.GetStatus"/>); prefer <see cref="GetHistoryCount"/> for new
    /// per-session call sites.</summary>
    public int HistoryCount => GetHistoryCount(null);

    public SidecarAgentChatService(
        IAgentModelClient modelClient,
        UserSettingsRepository settings,
        AgentSessionStore sessions,
        AgentSystemPromptBuilder promptBuilder,
        AgentToolCallOrchestrator toolOrchestrator,
        AgentContextBudgetPlanner budgetPlanner,
        Acp.AcpAgentHost? acpHost = null)
    {
        _modelClient = modelClient;
        _settings = settings;
        _sessions = sessions;
        _promptBuilder = promptBuilder;
        _toolOrchestrator = toolOrchestrator;
        _budgetPlanner = budgetPlanner;
        _acpHost = acpHost;
    }

    /// <summary>Composition-root convenience overload that builds the default collaborators from the
    /// same five services this class was originally constructed from. Kept so the DI registration
    /// (and every existing call site) keeps working without each collaborator having to be registered
    /// separately; prefer the collaborator-taking constructor when you need to substitute one.</summary>
    public SidecarAgentChatService(
        IAgentModelClient modelClient,
        IAgentToolRegistry toolRegistry,
        ProfileRepository profiles,
        UserSettingsRepository settings,
        DemoModeService demo)
        : this(
            modelClient,
            settings,
            new AgentSessionStore(),
            new AgentSystemPromptBuilder(profiles, demo),
            new AgentToolCallOrchestrator(toolRegistry),
            new AgentContextBudgetPlanner(modelClient))
    {
    }

    public int GetHistoryCount(string? sessionId) => _sessions.GetHistoryCount(sessionId);

    /// <summary>
    /// Seeds a brand-new session (workspace-intelligence Module 4's proactive insights) with a
    /// synthetic user/assistant exchange representing the fired alert and its background
    /// investigation, so opening this <paramref name="sessionId"/> through the normal chat
    /// endpoints immediately shows what was found — no separate "insight" viewer needed, and any
    /// follow-up question the user asks continues through the exact same turn-taking logic as any
    /// other session. A no-op safeguard: does nothing if a session with this id already exists,
    /// since the id is derived from the firing event's own identity (rule id + fired-at) and should
    /// never be seeded twice.
    /// </summary>
    public void SeedProactiveInsightSession(string sessionId, string ruleName, string alertMessage, string reportJson, string summary)
    {
        var session = _sessions.CreateIfAbsent(sessionId);
        if (session is null)
            return;

        session.History.Enqueue(new AgentMessage
        {
            Role = "user",
            Content = $"A monitoring alert just fired — \"{ruleName}\": {alertMessage}. What's related, and what should I check?",
        });
        session.History.Enqueue(new AgentMessage
        {
            Role = "assistant",
            Content = $"{summary}\n\nFull correlation report:\n{reportJson}",
        });
    }

    /// <inheritdoc cref="AgentSessionStore.GetEstimatedTokens"/>
    public int GetEstimatedTokens(string? sessionId) => _sessions.GetEstimatedTokens(sessionId);

    /// <summary>
    /// Percentage of the active profile's (effective) context window the most recently sent
    /// request for this session actually used, per the fully-constructed-request estimate computed
    /// in <see cref="AgentContextBudgetPlanner.PrepareHistoryForModelAsync"/> —
    /// workspace-intelligence Module 5/6. 0 for a session that has never sent a turn yet.
    /// </summary>
    public double GetContextUsagePercent(string? sessionId)
    {
        if (!_sessions.TryGet(sessionId, out var session) || session.LastContextWindowTokens <= 0)
            return 0;

        return Math.Round(100.0 * session.LastRequestEstimatedTokens / session.LastContextWindowTokens, 1);
    }

    /// <summary>
    /// Percentage of the effective context window at which the UI should start warning the user that
    /// the conversation is getting full — the same scaled threshold rolling summarization actually
    /// uses, so the visual cue and the backend's graceful-degradation point coincide.
    /// </summary>
    public double GetContextUsageWarningPercent(string? sessionId)
    {
        var contextWindow = GetEffectiveContextWindow(sessionId);
        return Math.Round(100.0 * AgentContextBudgetPlanner.ResolveSummarizationThreshold(contextWindow), 1);
    }

    private int GetEffectiveContextWindow(string? sessionId)
    {
        if (_sessions.TryGet(sessionId, out var session) && session.LastContextWindowTokens > 0)
            return session.LastContextWindowTokens;

        var profile = _settings.Settings.Agent.GetActiveProfile();
        return AgentContextBudgetPlanner.ResolveContextWindow(profile);
    }

    /// <summary>Clears the SwebKit-side history mirror and drops the session's ACP session (for
    /// ACP profiles — the agent owns its own transcript, so forgetting ours alone would leave the
    /// agent still holding the conversation). A no-op against the host for non-ACP profiles.</summary>
    public async Task ClearHistoryAsync(string? sessionId = null)
    {
        _sessions.ClearHistory(sessionId);
        if (_acpHost is not null)
            await _acpHost.DropSessionAsync(AgentSessionStore.Key(sessionId));
    }

    /// <summary>Overload preserving the pre-Module-5 call shape: no session, no context, and the
    /// safe "ask" mode (not "ask_and_do" — see <see cref="AgentToolCallOrchestrator"/>'s doc comment
    /// on why unspecified always means the narrower option).</summary>
    public Task<SidecarAgentReply> SendAsync(string userMessage, CancellationToken ct = default) =>
        SendAsync(null, userMessage, context: null, mode: null, scope: null, ct);

    public async Task<SidecarAgentReply> SendAsync(
        string? sessionId,
        string userMessage,
        AgentChatContext? context = null,
        string? mode = null,
        string? scope = null,
        CancellationToken ct = default)
    {
        var (session, request, steps, toolExecutor, summarized, sw) =
            await BeginTurnAsync(sessionId, userMessage, context, mode, scope, ct);

        try
        {
            var result = await _modelClient.ChatAsync(request, toolExecutor, ct);
            _sessions.Append(session, new AgentMessage { Role = "assistant", Content = result.Text });

            // Providers that run their own tool loop (ACP) report steps on the result instead of
            // through the step-tracking executor — merge them so the reasoning trace stays whole.
            if (result.Steps is { Count: > 0 })
                steps.AddRange(result.Steps);

            sw.Stop();
            return new SidecarAgentReply
            {
                Text = result.Text,
                ToolsUsed = result.ToolsUsed,
                Steps = steps,
                ElapsedMs = (int)sw.Elapsed.TotalMilliseconds,
                Status = result.HitMaxRounds ? "failed" : "done",
                Error = false,
                Summarized = summarized,
                ContextUsagePercent = GetContextUsagePercent(sessionId),
            };
        }
        catch (Exception ex)
        {
            _sessions.Append(session, new AgentMessage { Role = "assistant", Content = $"Error: {ex.Message}" });

            sw.Stop();
            return new SidecarAgentReply
            {
                Text = $"Error: {ex.Message}",
                Steps = steps,
                ElapsedMs = (int)sw.Elapsed.TotalMilliseconds,
                Status = "failed",
                Error = true,
                Summarized = summarized,
                ContextUsagePercent = GetContextUsagePercent(sessionId),
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
        string? scope = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (session, request, steps, toolExecutor, summarized, _) =
            await BeginTurnAsync(sessionId, userMessage, context, mode, scope, ct);

        var stream = _modelClient.ChatStreamAsync(request, toolExecutor, ct);
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
                    _sessions.Append(session, new AgentMessage { Role = "assistant", Content = $"Error: {caught.Message}" });
                    yield return new AgentStreamEvent
                    {
                        Kind = AgentStreamEventKind.Error,
                        ErrorMessage = caught.Message,
                        Steps = steps,
                        Summarized = summarized,
                        ContextUsagePercent = GetContextUsagePercent(sessionId),
                    };
                    yield break;
                }

                if (!hasNext)
                    yield break;

                if (current!.Kind == AgentStreamEventKind.Done && current.Result is not null)
                {
                    _sessions.Append(session, new AgentMessage { Role = "assistant", Content = current.Result.Text });
                    if (current.Result.Steps is { Count: > 0 })
                        steps.AddRange(current.Result.Steps);
                    yield return new AgentStreamEvent
                    {
                        Kind = AgentStreamEventKind.Done,
                        Result = current.Result,
                        Steps = steps,
                        Summarized = summarized,
                        ContextUsagePercent = GetContextUsagePercent(sessionId),
                    };
                    continue;
                }

                if (current.Kind == AgentStreamEventKind.Error)
                {
                    _sessions.Append(session, new AgentMessage { Role = "assistant", Content = $"Error: {current.ErrorMessage}" });
                    yield return new AgentStreamEvent
                    {
                        Kind = AgentStreamEventKind.Error,
                        ErrorMessage = current.ErrorMessage,
                        Steps = steps,
                        Summarized = summarized,
                        ContextUsagePercent = GetContextUsagePercent(sessionId),
                    };
                    continue;
                }

                yield return current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    /// <summary>Everything the streaming and non-streaming paths do identically before the model is
    /// called: sweep idle sessions, resolve/touch this session, normalize the mode and scope gates,
    /// build the system prompt and tool set, record the user message, and fit the history to the
    /// context budget. Kept as one step so the two paths can't drift apart — they were near-identical
    /// copies before, which is exactly how a fix applied to one and not the other gets lost.</summary>
    private async Task<TurnSetup> BeginTurnAsync(
        string? sessionId,
        string userMessage,
        AgentChatContext? context,
        string? mode,
        string? scope,
        CancellationToken ct)
    {
        _sessions.EvictIdleSessions();
        var session = _sessions.GetOrCreate(AgentSessionStore.Key(sessionId));
        session.LastActivity = DateTimeOffset.UtcNow;

        var normalizedMode = AgentToolCallOrchestrator.NormalizeMode(mode);
        var normalizedScope = AgentToolCallOrchestrator.NormalizeScope(scope);

        // Started here, not at the top of SendAsync: the reported ElapsedMs deliberately excludes the
        // idle-session sweep and covers the turn itself (prompt build, summarization, model call).
        var sw = Stopwatch.StartNew();
        var profile = _settings.Settings.Agent.GetActiveProfile();
        var hasToolCalling = (profile?.Capability ?? AgentCapability.Unknown) >= AgentCapability.ToolCalling;
        var systemPrompt = _promptBuilder.Build(context, normalizedMode, hasToolCalling);
        var tools = _toolOrchestrator.ResolveTools(hasToolCalling, normalizedMode, context, normalizedScope);

        // Record user message
        _sessions.Append(session, new AgentMessage { Role = "user", Content = userMessage });

        var (historyList, summarized) =
            await _budgetPlanner.PrepareHistoryForModelAsync(session, systemPrompt, tools, userMessage, profile, ct);

        var request = new AgentModelRequest
        {
            SystemPrompt = systemPrompt,
            UserMessage = userMessage,
            Tools = tools,
            History = historyList,
            SessionKey = AgentSessionStore.Key(sessionId),
        };

        var steps = new List<AgentChatStep>();
        var toolExecutor = _toolOrchestrator.BuildStepTrackingToolExecutor(tools, steps);

        return new TurnSetup(session, request, steps, toolExecutor, summarized, sw);
    }

    private sealed record TurnSetup(
        AgentConversationSession Session,
        AgentModelRequest Request,
        List<AgentChatStep> Steps,
        Func<string, JsonElement, CancellationToken, Task<string>>? ToolExecutor,
        bool Summarized,
        Stopwatch Elapsed);
}

/// <summary>What the user currently has open, passed by a contextual assistant panel so the model
/// can be told exactly what's on screen and so tool visibility can be scoped to that one area (see
/// <c>AgentToolCallOrchestrator.ResolveTools</c>). The global <c>/agent</c> page passes null.</summary>
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

    /// <summary>Per-tool-call trace for this turn (workspace-intelligence Module 6) — empty when no
    /// tools were used. See <c>AgentChatStep</c> (<c>SwebKit.Agents</c>) for the shape, reused from
    /// the legacy MAUI-side <c>AgentChatService</c> rather than inventing a new one.</summary>
    public IReadOnlyList<AgentChatStep> Steps { get; init; } = [];

    public int ElapsedMs { get; init; }
    public string Status { get; init; } = "done";
    public bool Error { get; init; }

    /// <summary>True when this turn's history was rolling-summarized before being sent — the
    /// frontend surfaces this as an inline notice (workspace-intelligence Module 5/6).</summary>
    public bool Summarized { get; init; }

    /// <summary>Percentage of the effective context window this turn's request used (see
    /// <see cref="SidecarAgentChatService.GetContextUsagePercent"/>).</summary>
    public double ContextUsagePercent { get; init; }
}
