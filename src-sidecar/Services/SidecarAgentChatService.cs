using System.Collections.Concurrent;
using System.Diagnostics;
using SwebKit.Agents;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Sidecar-specific agent chat service that wraps <see cref="IAgentModelClient"/>
/// with conversation history and context built from <see cref="ProfileRepository"/>.
/// Maintains a single conversation session (singleton lifetime, single-user desktop app).
/// </summary>
public sealed class SidecarAgentChatService
{
    private readonly IAgentModelClient _modelClient;
    private readonly IAgentToolRegistry _toolRegistry;
    private readonly ProfileRepository _profiles;
    private readonly UserSettingsRepository _settings;
    private readonly DemoModeService _demo;
    private readonly ConcurrentQueue<AgentMessage> _history = new();
    private readonly int _maxHistory = 20;

    public int HistoryCount => _history.Count;

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

    public void ClearHistory()
    {
        while (_history.TryDequeue(out _)) { }
    }

    public async Task<SidecarAgentReply> SendAsync(string userMessage, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var systemPrompt = BuildSystemPrompt();
        var profile = _settings.Settings.Agent.GetActiveProfile();

        // Record user message
        _history.Enqueue(new AgentMessage { Role = "user", Content = userMessage });
        TrimHistory();

        var historyList = _history.ToList();
        // Remove the last (user message) from history passed to the model since it's passed separately
        if (historyList.Count > 0)
            historyList.RemoveAt(historyList.Count - 1);

        var allTools = _toolRegistry.GetDefinitions();
        var hasToolCalling = (profile?.Capability ?? AgentCapability.Unknown) >= AgentCapability.ToolCalling;
        var tools = hasToolCalling ? allTools : [];

        var request = new AgentModelRequest
        {
            SystemPrompt = systemPrompt,
            UserMessage = userMessage,
            Tools = tools,
            History = historyList,
            Temperature = profile?.Temperature ?? 0.7,
            MaxTokens = profile?.MaxTokens ?? 2048,
        };

        try
        {
            var result = await _modelClient.ChatAsync(
                request,
                tools.Count > 0 ? (toolName, args, toolCt) => _toolRegistry.ExecuteAsync(toolName, args, toolCt) : null,
                ct);
            _history.Enqueue(new AgentMessage { Role = "assistant", Content = result.Text });
            TrimHistory();

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
            _history.Enqueue(new AgentMessage { Role = "assistant", Content = $"Error: {ex.Message}" });
            TrimHistory();

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

    private void TrimHistory()
    {
        while (_history.Count > _maxHistory && _history.TryDequeue(out _)) { }
    }

    private string BuildSystemPrompt()
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

        var context = contextParts.Count == 0
            ? "No workspace services configured."
            : string.Join(" | ", contextParts);

        var profile = _settings.Settings.Agent.GetActiveProfile();
        var hasToolCalling = (profile?.Capability ?? AgentCapability.Unknown) >= AgentCapability.ToolCalling;

        var toolPolicy = hasToolCalling
            ? """
              ## Tool policy
              - Use tools to fetch live data when the user asks about Kubernetes pods/namespaces/events/logs
                or Service Bus queue stats, messages, or health.
              - All available tools are read-only diagnostics — no confirmation is needed before calling them.
              - If a tool returns an error, explain what it means and suggest a resolution.
              - Do not expose internal JSON schemas or tool names in your replies.
              """
            : """
              ## Tool policy
              - Tool calling is not available with the current model. Answer based on context only.
              - If the user needs live data, suggest enabling a model that supports tool calling.
              """;

        return $"""
            You are SwebKit Assistant, an AI copilot embedded in SwebKit — a DevOps operations desktop
            application for platform engineers. You help users diagnose and understand their Kubernetes
            clusters, Azure DevOps pipelines, Redis instances, Azure Service Bus queues, Storage accounts,
            and observability data.

            ## Current workspace context
            {context}

            ## Response format
            - Be concise and technical. Prefer bullet points and tables over prose.
            - If you are unsure, say so rather than guessing.

            {toolPolicy}

            ## Limits
            - No Observability, Storage, or API Client tools are available in the sidecar mode yet.
            - No Git operations.
            """;
    }
}

public sealed class SidecarAgentReply
{
    public required string Text { get; init; }
    public IReadOnlyList<string> ToolsUsed { get; init; } = [];
    public int ElapsedMs { get; init; }
    public string Status { get; init; } = "done";
    public bool Error { get; init; }
}
