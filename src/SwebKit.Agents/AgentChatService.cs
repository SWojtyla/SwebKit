using System.Diagnostics;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents;

/// <summary>
/// Default implementation of <see cref="IAgentChatService"/>.
/// Wraps <see cref="IAgentModelClient"/> with a persistent <see cref="ConversationSession"/>
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

    private readonly IAgentModelClient _modelClient;
    private readonly IAgentToolRegistry _registry;
    private readonly AppStateService _appState;
    private readonly UserSettingsRepository _settings;
    private readonly IAgentContextBuilder _contextBuilder;
    private ConversationSession _session;

    public AgentChatService(
        IAgentModelClient modelClient,
        IAgentToolRegistry registry,
        AppStateService appState,
        UserSettingsRepository settings,
        IAgentContextBuilder contextBuilder)
    {
        _modelClient = modelClient;
        _registry = registry;
        _appState = appState;
        _settings = settings;
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

        // Record the user message in history before sending
        _session.Add(new AgentMessage { Role = "user", Content = userMessage });

        var request = new AgentModelRequest
        {
            SystemPrompt = systemPrompt,
            UserMessage = userMessage,
            Tools = tools,
            History = _session.Messages.Take(_session.Count - 1).ToList(), // all except the user message we just added
            Temperature = _settings.Settings.Agent.GetActiveProfile()?.Temperature ?? 0.7,
            MaxTokens = _settings.Settings.Agent.GetActiveProfile()?.MaxTokens ?? 2048,
        };

        var result = await _modelClient.ChatAsync(
            request,
            async (toolName, args, toolCt) =>
            {
                toolsUsed.Add(toolName);
                return await _registry.ExecuteAsync(toolName, args, toolCt);
            },
            ct);

        // Record the assistant response and any intermediate messages in history
        // The client handles the agentic loop internally; we only get the final text.
        // We add the final assistant message to history.
        _session.Add(new AgentMessage { Role = "assistant", Content = result.Text });

        sw.Stop();

        return new AgentChatReply
        {
            Text = result.Text,
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
