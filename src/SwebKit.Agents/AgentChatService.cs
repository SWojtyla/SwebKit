using System.Diagnostics;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents;

/// <summary>
/// Default implementation of <see cref="IAgentChatService"/>.
/// Wraps <see cref="IMistralClient"/> with a persistent <see cref="ConversationSession"/>
/// and builds a context-aware system prompt from the current workspace configuration.
/// </summary>
public sealed class AgentChatService : IAgentChatService
{
    private static readonly string SystemPromptTemplate =
        """
        You are SwebKit Assistant, an AI copilot embedded in SwebKit — a DevOps operations desktop
        application for platform engineers. You help users diagnose and understand their Kubernetes
        clusters, Azure DevOps pipelines, Redis instances, Azure Service Bus queues, and
        observability data.

        Current workspace context:
        {CONTEXT}

        Guidelines:
        - Be concise and technical. Prefer bullet points and tables over prose.
        - When a user asks about pods, events, or logs, use the available tools to fetch live data.
        - If a tool returns an error, explain what it means and suggest a resolution.
        - Do not expose internal JSON schemas or tool names in your replies.
        - If you are unsure, say so rather than guessing.
        """;

    private readonly IMistralClient _mistral;
    private readonly IAgentToolRegistry _registry;
    private readonly AppStateService _appState;
    private readonly UserSettingsRepository _settings;
    private readonly MistralConfig _mistralConfig;
    private readonly IAgentContextBuilder _contextBuilder;
    private readonly ConversationSession _session;

    public AgentChatService(
        IMistralClient mistral,
        IAgentToolRegistry registry,
        AppStateService appState,
        UserSettingsRepository settings,
        MistralConfig mistralConfig,
        IAgentContextBuilder contextBuilder)
    {
        _mistral = mistral;
        _registry = registry;
        _appState = appState;
        _settings = settings;
        _mistralConfig = mistralConfig;
        _contextBuilder = contextBuilder;

        var maxMessages = _settings.Settings.Agent.MaxHistoryMessages;
        _session = new ConversationSession(maxMessages);
    }

    public int HistoryMessageCount => _session.Count;

    public bool IsNearHistoryLimit => _session.IsNearLimit;

    public void ClearHistory() => _session.Clear();

    public async Task<AgentChatReply> SendAsync(string userMessage, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var toolsUsed = new List<string>();

        var systemPrompt = BuildSystemPrompt();
        var tools = _registry.GetDefinitions();

        var history = _session.Messages as List<object>
            ?? new List<object>(_session.Messages);

        var reply = await _mistral.ChatAsync(
            systemPrompt,
            userMessage,
            tools,
            history,
            async (toolName, args, toolCt) =>
            {
                toolsUsed.Add(toolName);
                return await _registry.ExecuteAsync(toolName, args, toolCt);
            },
            ct);

        sw.Stop();

        return new AgentChatReply
        {
            Text = reply,
            ToolsUsed = toolsUsed,
            Elapsed = sw.Elapsed
        };
    }

    private string BuildSystemPrompt()
    {
        var context = _contextBuilder.BuildContext(_appState);
        return SystemPromptTemplate.Replace("{CONTEXT}", context);
    }
}
