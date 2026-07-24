using System.Diagnostics;
using SwebKit.Agents.Tools;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Agents;

/// <summary>
/// Default implementation of <see cref="IAgentChatService"/>.
/// Wraps <see cref="IAgentModelClient"/> with a persistent <see cref="ConversationSession"/>
/// and builds a context-aware system prompt from the current workspace configuration.
/// </summary>
public sealed class AgentChatService : IAgentChatService
{
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
        var steps = new List<AgentChatStep>();

        var systemPrompt = BuildSystemPrompt();
        var allTools = _registry.GetDefinitions();

        // Filter tools based on active profile capability
        var profile = _settings.Settings.Agent.GetActiveProfile();
        var activeCapability = profile?.Capability ?? AgentCapability.Unknown;
        var tools = FilterToolsByCapability(allTools, activeCapability);

        // Record the user message in history before sending
        _session.Add(new AgentMessage { Role = "user", Content = userMessage });

        var request = new AgentModelRequest
        {
            SystemPrompt = systemPrompt,
            UserMessage = userMessage,
            Tools = tools,
            History = _session.Messages.Take(_session.Count - 1).ToList(),
            Temperature = profile?.Temperature ?? 0.7,
            MaxTokens = profile?.MaxTokens ?? 2048,
        };

        AgentChatResult result;
        try
        {
            result = await _modelClient.ChatAsync(
                request,
                async (toolName, args, toolCt) =>
                {
                    var toolSw = Stopwatch.StartNew();
                    toolsUsed.Add(toolName);

                    // Find tool metadata for step tracking
                    var toolDef = allTools.FirstOrDefault(t => t.Name == toolName);
                    steps.Add(new AgentChatStep
                    {
                        Type = "tool_call",
                        ToolName = toolName,
                        Summary = toolDef?.Kind == ToolKind.Mutate
                            ? $"Preparing {toolName} (mutation)"
                            : $"Calling {toolName}",
                    });

                    var toolResult = await _registry.ExecuteAsync(toolName, args, toolCt);
                    toolSw.Stop();

                    steps.Add(new AgentChatStep
                    {
                        Type = "tool_result",
                        ToolName = toolName,
                        Summary = SummarizeToolResult(toolResult),
                        Elapsed = toolSw.Elapsed
                    });

                    return toolResult;
                },
                ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _session.Add(new AgentMessage { Role = "assistant", Content = $"Error: {ex.Message}" });
            return new AgentChatReply
            {
                Text = $"Error: {ex.Message}",
                ToolsUsed = toolsUsed,
                Steps = steps,
                Elapsed = sw.Elapsed,
                Status = AgentStatus.Failed
            };
        }

        // Record the assistant response in history
        _session.Add(new AgentMessage { Role = "assistant", Content = result.Text });

        sw.Stop();

        var status = result.HitMaxRounds ? AgentStatus.Failed : AgentStatus.Done;

        return new AgentChatReply
        {
            Text = result.Text,
            ToolsUsed = toolsUsed,
            Steps = steps,
            Elapsed = sw.Elapsed,
            Status = status
        };
    }

    private string BuildSystemPrompt()
    {
        var context = _contextBuilder.BuildContext(_appState);
        var profile = _settings.Settings.Agent.GetActiveProfile();
        var capability = profile?.Capability ?? AgentCapability.Unknown;
        var hasTools = capability >= AgentCapability.ToolCalling;

        var toolPolicy = hasTools
            ? """
              ## Tool policy
              - Use tools to fetch live data when the user asks about pods, events, logs, queues, or metrics.
              - Read-only tools are safe to call directly.
              - Mutating tools produce a proposal that the user must confirm before any change is applied.
              - If a tool returns an error, explain what it means and suggest a resolution.
              - Do not expose internal JSON schemas or tool names in your replies.
              """
            : """
              ## Tool policy
              - Tool calling is not available with the current model. Answer based on context only.
              - If the user needs live data, suggest enabling a model that supports tool calling.
              """;

        var confirmationPolicy = hasTools
            ? """
              ## Confirmation policy
              - Any mutation (create, update, delete, execute) requires explicit user confirmation.
              - Never assume consent from an ambiguous "yes" — wait for the confirmation card.
              - Describe what will change before proposing the action.
              """
            : "";

        return $"""
            You are SwebKit Assistant, an AI copilot embedded in SwebKit — a DevOps operations desktop
            application for platform engineers. You help users diagnose and understand their Kubernetes
            clusters, Azure DevOps pipelines, Redis instances, Azure Service Bus queues, and
            observability data.

            ## Current workspace context
            {context}

            ## Response format
            - Be concise and technical. Prefer bullet points and tables over prose.
            - If you are unsure, say so rather than guessing.

            {toolPolicy}
            {(hasTools && !string.IsNullOrEmpty(confirmationPolicy) ? confirmationPolicy + "\n" : "")}
            ## Limits
            - REST API Client operations are limited to V1 scope: collections, folders, and requests.
            - No agent management of environments, variables, auth, GraphQL, or WebSocket.
            - No Git operations.
            """;
    }

    private static IReadOnlyList<ToolDefinition> FilterToolsByCapability(
        IReadOnlyList<ToolDefinition> tools,
        AgentCapability capability)
    {
        if (capability >= AgentCapability.ToolCalling)
            return tools;

        // Chat-only mode: no tools available
        return [];
    }

    private static string SummarizeToolResult(string result)
    {
        if (string.IsNullOrEmpty(result))
            return "Empty result";

        // Truncate for step summary — don't expose full result in step log
        return result.Length > 80 ? result[..80] + "…" : result;
    }
}
