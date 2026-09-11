using SwebKit.Agents;
using SwebKit.Core.Domain;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Decides how much of a session's transcript actually fits in the active profile's context window
/// for the turn about to be sent, and rolls the overflow up into a summary when it doesn't
/// (workspace-intelligence Module 5/7). Everything token-budget related lives here so the chat
/// service itself stays a coordinator.
/// </summary>
public sealed class AgentContextBudgetPlanner
{
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

    public AgentContextBudgetPlanner(IAgentModelClient modelClient)
    {
        _modelClient = modelClient;
    }

    /// <summary>
    /// Builds the history actually sent to the model for this turn — the just-enqueued user message
    /// excluded, since it's passed separately — trimming it via rolling summarization first if the
    /// fully-constructed request (system prompt + tool schemas + history + this message) would
    /// otherwise cross the profile's scaled summarization threshold (workspace-intelligence Module 7:
    /// smaller <see cref="AgentProfile.ContextWindowTokens"/> values trigger earlier summarization).
    /// Also records the estimate actually used (post-trim, if a trim happened) onto the session for
    /// <c>SidecarAgentChatService.GetContextUsagePercent</c>.
    /// </summary>
    public async Task<(List<AgentMessage> HistoryForModel, bool Summarized)> PrepareHistoryForModelAsync(
        AgentConversationSession session,
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

    public static int ResolveContextWindow(AgentProfile? profile) =>
        profile?.ContextWindowTokens is > 0 ? profile.ContextWindowTokens.Value : DefaultContextWindowTokens;

    /// <summary>
    /// Rolling-summarization trigger point as a fraction of the effective context window — scaled to
    /// the profile's declared <see cref="AgentProfile.ContextWindowTokens"/> (workspace-intelligence
    /// Module 7). Smaller windows summarize earlier, leaving headroom for flaky local models; large
    /// cloud windows keep the original 75% value. The scale is clamped to a sane 0.50–0.75 band so a
    /// typo or 1-token window doesn't produce a pathological threshold.
    /// </summary>
    public static double ResolveSummarizationThreshold(int contextWindowTokens)
    {
        if (contextWindowTokens <= SmallContextWindowTokens)
            return MinSummarizationThresholdRatio;
        if (contextWindowTokens >= LargeContextWindowTokens)
            return MaxSummarizationThresholdRatio;

        var ratio = (double)(contextWindowTokens - SmallContextWindowTokens) /
            (LargeContextWindowTokens - SmallContextWindowTokens);
        return MinSummarizationThresholdRatio + ratio * (MaxSummarizationThresholdRatio - MinSummarizationThresholdRatio);
    }

    private static List<AgentMessage> HistoryExcludingLastMessage(AgentConversationSession session)
    {
        var historyList = session.History.ToList();
        if (historyList.Count > 0)
            historyList.RemoveAt(historyList.Count - 1);
        return historyList;
    }

    /// <summary>~4-chars-per-token heuristic applied to the *fully constructed* request (system
    /// prompt + tool schemas + history + the pending user message) — not just history, unlike the
    /// coarser <see cref="AgentSessionStore.GetEstimatedTokens"/> — since one large tool result or a
    /// long tool-schema list can matter as much as the transcript itself.</summary>
    internal static int EstimateFullRequestTokens(
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
    /// in <see cref="AgentSystemPromptBuilder.Build"/>), so it survives a summarization pass
    /// automatically — no special-casing needed to "pin" it. Fails open: if the summarization call
    /// itself throws (e.g. a flaky local model), the turn proceeds with the untrimmed history rather
    /// than failing what's meant to be a graceful-degradation feature.
    /// </summary>
    private async Task<bool> TrySummarizeOlderHistoryAsync(AgentConversationSession session, CancellationToken ct)
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
}
