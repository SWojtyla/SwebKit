using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
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

    /// <summary>workspace-intelligence Module 3's escalation value for <c>AgentChatRequest.Scope</c>
    /// — orthogonal to <see cref="AskAndDoMode"/> (mode gates mutate tools; scope gates which area's
    /// tools are visible at all). "feature" (the default) leaves the existing per-area filter in
    /// place; "workspace" skips it for the turn.</summary>
    private const string WorkspaceScope = "workspace";
    private static readonly TimeSpan IdleSessionTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Context window assumed for a profile that never reported one (a local model whose
    /// actual window LM Studio doesn't advertise, and the user hasn't set by hand) — deliberately
    /// conservative rather than treating "unknown" as "unlimited", per workspace-intelligence
    /// Module 5.</summary>
    private const int DefaultContextWindowTokens = 4096;

    /// <summary>Most recent messages kept verbatim across a summarization pass — 3 user/assistant
    /// exchanges. Below this count there's nothing older to summarize away.</summary>
    private const int KeepVerbatimMessageCount = 6;

    /// <summary>Small-window reference point for the rolling-summarization threshold scale
    /// (workspace-intelligence Module 7). A window at or below this value triggers summarization at
    /// <see cref="MinSummarizationThresholdRatio"/> — earlier and more aggressively than a large
    /// cloud model, so tiny local windows don't get pushed to the edge.</summary>
    private const int SmallContextWindowTokens = 4096;

    /// <summary>Large-window reference point for the rolling-summarization threshold scale.
    /// A window at or above this value keeps the original 75% threshold (Module 5).</summary>
    private const int LargeContextWindowTokens = 131072;

    /// <summary>Minimum rolling-summarization threshold for the smallest windows — summarize at 50%
    /// of an unknown/tiny local window to leave plenty of headroom.</summary>
    private const double MinSummarizationThresholdRatio = 0.50;

    /// <summary>Maximum rolling-summarization threshold for the largest windows — the original
    /// Module 5 value, giving big cloud models the full benefit of their declared context.</summary>
    private const double MaxSummarizationThresholdRatio = 0.75;

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
        if (_sessions.ContainsKey(sessionId))
            return;

        var session = _sessions.GetOrAdd(sessionId, _ => new ConversationSession());
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

    /// <summary>
    /// Percentage of the active profile's (effective) context window the most recently sent
    /// request for this session actually used, per the fully-constructed-request estimate computed
    /// in <see cref="PrepareHistoryForModelAsync"/> — workspace-intelligence Module 5/6. 0 for a
    /// session that has never sent a turn yet.
    /// </summary>
    public double GetContextUsagePercent(string? sessionId)
    {
        if (!_sessions.TryGetValue(Key(sessionId), out var session) || session.LastContextWindowTokens <= 0)
            return 0;

        return Math.Round(100.0 * session.LastRequestEstimatedTokens / session.LastContextWindowTokens, 1);
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
        SendAsync(null, userMessage, context: null, mode: null, scope: null, ct);

    public async Task<SidecarAgentReply> SendAsync(
        string? sessionId,
        string userMessage,
        AgentChatContext? context = null,
        string? mode = null,
        string? scope = null,
        CancellationToken ct = default)
    {
        EvictIdleSessions();
        var key = Key(sessionId);
        var session = _sessions.GetOrAdd(key, _ => new ConversationSession());
        session.LastActivity = DateTimeOffset.UtcNow;

        var normalizedMode = mode == AskAndDoMode ? AskAndDoMode : "ask";
        var normalizedScope = scope == WorkspaceScope ? WorkspaceScope : "feature";

        var sw = Stopwatch.StartNew();
        var profile = _settings.Settings.Agent.GetActiveProfile();
        var hasToolCalling = (profile?.Capability ?? AgentCapability.Unknown) >= AgentCapability.ToolCalling;
        var systemPrompt = BuildSystemPrompt(context, normalizedMode, hasToolCalling);
        var tools = ResolveTools(hasToolCalling, normalizedMode, context, normalizedScope);

        // Record user message
        session.History.Enqueue(new AgentMessage { Role = "user", Content = userMessage });
        TrimHistory(session);

        var (historyList, summarized) = await PrepareHistoryForModelAsync(session, systemPrompt, tools, userMessage, profile, ct);

        var request = new AgentModelRequest
        {
            SystemPrompt = systemPrompt,
            UserMessage = userMessage,
            Tools = tools,
            History = historyList,
        };

        var steps = new List<AgentChatStep>();
        var toolExecutor = BuildStepTrackingToolExecutor(tools, steps);

        try
        {
            var result = await _modelClient.ChatAsync(request, toolExecutor, ct);
            session.History.Enqueue(new AgentMessage { Role = "assistant", Content = result.Text });
            TrimHistory(session);

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
            session.History.Enqueue(new AgentMessage { Role = "assistant", Content = $"Error: {ex.Message}" });
            TrimHistory(session);

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
        EvictIdleSessions();
        var key = Key(sessionId);
        var session = _sessions.GetOrAdd(key, _ => new ConversationSession());
        session.LastActivity = DateTimeOffset.UtcNow;

        var normalizedMode = mode == AskAndDoMode ? AskAndDoMode : "ask";
        var normalizedScope = scope == WorkspaceScope ? WorkspaceScope : "feature";

        var profile = _settings.Settings.Agent.GetActiveProfile();
        var hasToolCalling = (profile?.Capability ?? AgentCapability.Unknown) >= AgentCapability.ToolCalling;
        var systemPrompt = BuildSystemPrompt(context, normalizedMode, hasToolCalling);
        var tools = ResolveTools(hasToolCalling, normalizedMode, context, normalizedScope);

        session.History.Enqueue(new AgentMessage { Role = "user", Content = userMessage });
        TrimHistory(session);

        var (historyList, summarized) = await PrepareHistoryForModelAsync(session, systemPrompt, tools, userMessage, profile, ct);

        var request = new AgentModelRequest
        {
            SystemPrompt = systemPrompt,
            UserMessage = userMessage,
            Tools = tools,
            History = historyList,
        };

        var steps = new List<AgentChatStep>();
        var toolExecutor = BuildStepTrackingToolExecutor(tools, steps);

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
                    session.History.Enqueue(new AgentMessage { Role = "assistant", Content = $"Error: {caught.Message}" });
                    TrimHistory(session);
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
                    session.History.Enqueue(new AgentMessage { Role = "assistant", Content = current.Result.Text });
                    TrimHistory(session);
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
                    session.History.Enqueue(new AgentMessage { Role = "assistant", Content = $"Error: {current.ErrorMessage}" });
                    TrimHistory(session);
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

    /// <summary>Wraps the raw tool registry call with step recording — same "tool_call"/"tool_result"
    /// pair shape the legacy MAUI-side <c>AgentChatService.SendAsync</c> already uses, reused rather
    /// than inventing a new trace format (workspace-intelligence Module 6).</summary>
    private Func<string, JsonElement, CancellationToken, Task<string>>? BuildStepTrackingToolExecutor(
        IReadOnlyList<ToolDefinition> tools, List<AgentChatStep> steps)
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

            var result = await _toolRegistry.ExecuteAsync(toolName, args, toolCt);
            toolSw.Stop();

            steps.Add(new AgentChatStep
            {
                Type = "tool_result",
                ToolName = toolName,
                Summary = SummarizeToolResult(result),
                Elapsed = toolSw.Elapsed,
            });

            return result;
        };
    }

    private static string SummarizeToolResult(string result)
    {
        if (string.IsNullOrEmpty(result))
            return "Empty result";

        return result.Length > 80 ? result[..80] + "…" : result;
    }

    /// <summary>
    /// Builds the history actually sent to the model for this turn — the just-enqueued user message
    /// excluded, since it's passed separately — trimming it via rolling summarization first if the
    /// fully-constructed request (system prompt + tool schemas + history + this message) would
    /// otherwise cross the profile's scaled summarization threshold (workspace-intelligence Module 7:
    /// smaller <see cref="AgentProfile.ContextWindowTokens"/> values trigger earlier summarization).
    /// Also records the estimate actually used (post-trim, if a trim happened) onto the session for
    /// <see cref="GetContextUsagePercent"/>.
    /// </summary>
    private async Task<(List<AgentMessage> HistoryForModel, bool Summarized)> PrepareHistoryForModelAsync(
        ConversationSession session,
        string systemPrompt,
        IReadOnlyList<ToolDefinition> tools,
        string userMessage,
        AgentProfile? profile,
        CancellationToken ct)
    {
        var contextWindow = ResolveContextWindow(profile);
        var threshold = ResolveSummarizationThreshold(contextWindow);
        var historyForModel = HistoryExcludingLastMessage(session);
        var estimated = EstimateFullRequestTokens(systemPrompt, tools, historyForModel, userMessage);
        var summarized = false;

        if (estimated > contextWindow * threshold && historyForModel.Count > KeepVerbatimMessageCount)
        {
            summarized = await TrySummarizeOlderHistoryAsync(session, ct);
            if (summarized)
            {
                historyForModel = HistoryExcludingLastMessage(session);
                estimated = EstimateFullRequestTokens(systemPrompt, tools, historyForModel, userMessage);
            }
        }

        session.LastContextWindowTokens = contextWindow;
        session.LastRequestEstimatedTokens = estimated;

        return (historyForModel, summarized);
    }

    private static int ResolveContextWindow(AgentProfile? profile) =>
        profile?.ContextWindowTokens is > 0 ? profile.ContextWindowTokens.Value : DefaultContextWindowTokens;

    /// <summary>
    /// Rolling-summarization trigger point as a fraction of the effective context window — scaled to
    /// the profile's declared <see cref="AgentProfile.ContextWindowTokens"/> (workspace-intelligence
    /// Module 7). Smaller windows summarize earlier, leaving headroom for flaky local models; large
    /// cloud windows keep the original 75% value. The scale is clamped to a sane 0.50–0.75 band so a
    /// typo or 1-token window doesn't produce a pathological threshold.
    /// </summary>
    internal static double ResolveSummarizationThreshold(int contextWindowTokens)
    {
        if (contextWindowTokens <= SmallContextWindowTokens)
            return MinSummarizationThresholdRatio;
        if (contextWindowTokens >= LargeContextWindowTokens)
            return MaxSummarizationThresholdRatio;

        var ratio = (double)(contextWindowTokens - SmallContextWindowTokens) /
            (LargeContextWindowTokens - SmallContextWindowTokens);
        return MinSummarizationThresholdRatio + ratio * (MaxSummarizationThresholdRatio - MinSummarizationThresholdRatio);
    }

    /// <summary>
    /// Percentage of the effective context window at which the UI should start warning the user that
    /// the conversation is getting full — the same scaled threshold rolling summarization actually
    /// uses, so the visual cue and the backend's graceful-degradation point coincide.
    /// </summary>
    public double GetContextUsageWarningPercent(string? sessionId)
    {
        var contextWindow = GetEffectiveContextWindow(sessionId);
        return Math.Round(100.0 * ResolveSummarizationThreshold(contextWindow), 1);
    }

    private int GetEffectiveContextWindow(string? sessionId)
    {
        if (_sessions.TryGetValue(Key(sessionId), out var session) && session.LastContextWindowTokens > 0)
            return session.LastContextWindowTokens;

        var profile = _settings.Settings.Agent.GetActiveProfile();
        return ResolveContextWindow(profile);
    }

    private static List<AgentMessage> HistoryExcludingLastMessage(ConversationSession session)
    {
        var historyList = session.History.ToList();
        if (historyList.Count > 0)
            historyList.RemoveAt(historyList.Count - 1);
        return historyList;
    }

    /// <summary>~4-chars-per-token heuristic applied to the *fully constructed* request (system
    /// prompt + tool schemas + history + the pending user message) — not just history, unlike the
    /// coarser <see cref="GetEstimatedTokens"/> — since one large tool result or a long tool-schema
    /// list can matter as much as the transcript itself.</summary>
    private static int EstimateFullRequestTokens(
        string systemPrompt, IReadOnlyList<ToolDefinition> tools, IReadOnlyList<AgentMessage> history, string userMessage)
    {
        var chars = systemPrompt.Length + userMessage.Length;
        chars += history.Sum(m => (m.Content?.Length ?? 0) + (m.ToolCalls?.Sum(tc => tc.ArgumentsJson.Length) ?? 0));
        chars += tools.Sum(t => t.Name.Length + t.Description.Length + t.ParametersSchema.GetRawText().Length);
        return (int)Math.Ceiling(chars / 4.0);
    }

    /// <summary>
    /// Rolling summarization: keeps the most recent <see cref="KeepVerbatimMessageCount"/> messages
    /// verbatim, replaces everything older with a single short summary turn from one extra
    /// <see cref="IAgentModelClient.CompleteAsync"/> call. The "current focus"/workspace-context
    /// system prompt is never part of <c>session.History</c> at all (it's rebuilt fresh every turn
    /// in <see cref="BuildSystemPrompt"/>), so it survives a summarization pass automatically — no
    /// special-casing needed to "pin" it. Fails open: if the summarization call itself throws (e.g.
    /// a flaky local model), the turn proceeds with the untrimmed history rather than failing what's
    /// meant to be a graceful-degradation feature.
    /// </summary>
    private async Task<bool> TrySummarizeOlderHistoryAsync(ConversationSession session, CancellationToken ct)
    {
        var all = session.History.ToList();
        if (all.Count <= KeepVerbatimMessageCount)
            return false;

        var toSummarize = all.Take(all.Count - KeepVerbatimMessageCount).ToList();
        var toKeep = all.Skip(all.Count - KeepVerbatimMessageCount).ToList();

        string? summaryText;
        try
        {
            var summaryRequest = new AgentModelRequest
            {
                SystemPrompt = "Summarize the following conversation between a user and an AI assistant "
                    + "concisely, in under 150 words, preserving concrete facts (resource names, findings, "
                    + "decisions) a later turn might still need. Do not add commentary about the "
                    + "summarization itself.",
                UserMessage = string.Join("\n", toSummarize.Select(m => $"{m.Role}: {m.Content}")),
            };
            var response = await _modelClient.CompleteAsync(summaryRequest, ct);
            summaryText = response.Content;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(summaryText))
            return false;

        while (session.History.TryDequeue(out _)) { }
        session.History.Enqueue(new AgentMessage { Role = "system", Content = $"[Earlier conversation summarized]: {summaryText}" });
        foreach (var m in toKeep)
            session.History.Enqueue(m);

        return true;
    }

    /// <summary>Applies the three tool-visibility gates in order: capability (existing) → mode (new
    /// — "ask" keeps only Read-kind tools) → feature-area scope (new — when
    /// <paramref name="context"/> names an area, keeps only that area's tools, plus Observability's
    /// (exempt — see below); a request with no context, i.e. the global page, skips this gate
    /// entirely and keeps every area's tools, exactly like before Module 5).</summary>
    private IReadOnlyList<ToolDefinition> ResolveTools(bool hasToolCalling, string normalizedMode, AgentChatContext? context, string normalizedScope)
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
            tools = tools.Where(t => t.FeatureArea == area || t.FeatureArea == FeatureArea.Observability);
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

        /// <summary>Estimated token size of the most recently sent request for this session (post
        /// rolling-summarization trim, if one happened) — workspace-intelligence Module 5/6.</summary>
        public int LastRequestEstimatedTokens { get; set; }

        /// <summary>The effective context window (profile's declared value or the conservative
        /// default) that <see cref="LastRequestEstimatedTokens"/> was measured against.</summary>
        public int LastContextWindowTokens { get; set; }
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
