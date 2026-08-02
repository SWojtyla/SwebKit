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
///
/// Holds one <see cref="ConversationSession"/> per <c>sessionId</c> (a per-panel id the frontend
/// generates once per mounted contextual assistant, see <c>ai-augmented-app</c> technical-plan.md
/// Module 2), so a conversation opened from an AKS pod panel doesn't share history with one opened
/// from a Redis key panel, or with the global <c>/agent</c> page. A null/omitted <c>sessionId</c>
/// maps to a single fixed key (<see cref="GlobalSessionKey"/>) — this preserves the pre-Module-2
/// behavior of the global page exactly (one shared conversation, never evicted), which is why that
/// key is exempt from idle eviction while every other session isn't.
/// </summary>
public sealed class SidecarAgentChatService
{
    private const string GlobalSessionKey = "__global__";
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

    public void ClearHistory(string? sessionId = null)
    {
        if (_sessions.TryGetValue(Key(sessionId), out var session))
            while (session.History.TryDequeue(out _)) { }
    }

    /// <summary>Overload preserving the pre-Module-2 call shape for the global session.</summary>
    public Task<SidecarAgentReply> SendAsync(string userMessage, CancellationToken ct = default) =>
        SendAsync(null, userMessage, ct);

    public async Task<SidecarAgentReply> SendAsync(string? sessionId, string userMessage, CancellationToken ct = default)
    {
        EvictIdleSessions();
        var key = Key(sessionId);
        var session = _sessions.GetOrAdd(key, _ => new ConversationSession());
        session.LastActivity = DateTimeOffset.UtcNow;

        var sw = Stopwatch.StartNew();
        var systemPrompt = BuildSystemPrompt();
        var profile = _settings.Settings.Agent.GetActiveProfile();

        // Record user message
        session.History.Enqueue(new AgentMessage { Role = "user", Content = userMessage });
        TrimHistory(session);

        var historyList = session.History.ToList();
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

    private sealed class ConversationSession
    {
        public ConcurrentQueue<AgentMessage> History { get; } = new();
        public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.UtcNow;
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
