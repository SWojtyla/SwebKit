using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Builds the system prompt sent with every turn: the assistant's role, a coarse summary of the
/// workspace the user has configured (from <see cref="ProfileRepository"/>), the "current focus"
/// detail a contextual panel passes, and the tool policy matching the turn's capability/mode.
/// Rebuilt fresh per turn rather than accumulated in history — which is why it survives a rolling
/// summarization pass without any special "pinning".
/// </summary>
public sealed class AgentSystemPromptBuilder
{
    private readonly ProfileRepository _profiles;
    private readonly DemoModeService _demo;

    public AgentSystemPromptBuilder(ProfileRepository profiles, DemoModeService demo)
    {
        _profiles = profiles;
        _demo = demo;
    }

    public string Build(AgentChatContext? context, string normalizedMode, bool hasToolCalling)
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

        if (normalizedMode == AgentToolCallOrchestrator.AskAndDoMode)
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
}
