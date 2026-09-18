using SwebKit.Agents.Tools;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
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

    public string Build(AgentChatContext? context, string normalizedMode, string normalizedScope, bool hasToolCalling)
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

        var workspaceMap = BuildWorkspaceMapSection(config);

        var fencedAreas = BuildFencedAreasSection(data, config, context, normalizedScope, hasToolCalling);

        var toolPolicy = BuildToolPolicySection(hasToolCalling, normalizedMode);

        return $"""
            You are SwebKit Assistant, an AI copilot embedded in SwebKit — a DevOps operations desktop
            application for platform engineers. You help users diagnose and understand their Kubernetes
            clusters, Azure DevOps pipelines, Redis instances, Azure Service Bus queues, Storage accounts,
            and observability data.
            {currentFocus}
            ## Current workspace context
            {workspaceContext}
            {workspaceMap}
            {fencedAreas}
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

    /// <summary>Caps on the workspace-map section — the prompt is rebuilt per turn and feeds the
    /// context budget (<see cref="AgentContextBudgetPlanner"/> counts system-prompt chars), so a
    /// densely-curated map must overflow gracefully instead of eating the window.</summary>
    private const int MaxMapNodes = 30;
    private const int MaxMapEdges = 40;

    private static readonly Dictionary<WorkspaceResourceArea, string> MapAreaLabels = new()
    {
        [WorkspaceResourceArea.Aks] = "AKS",
        [WorkspaceResourceArea.ServiceBus] = "Service Bus",
        [WorkspaceResourceArea.Redis] = "Redis",
        [WorkspaceResourceArea.Sql] = "SQL",
        [WorkspaceResourceArea.Storage] = "Storage",
    };

    /// <summary>workspace-map-overhaul — the user-curated topology (Settings → Map) as context on
    /// EVERY turn, not just when the model happens to call a tool. Until now the map was only
    /// reachable through <c>investigate_workspace_issue</c>, which is fenced behind workspace scope
    /// on contextual panels — so the declared relationships were invisible on most turns. Rendered
    /// compactly: nodes grouped by area, edges as "from → to (label)". Empty when no nodes exist —
    /// an unconfigured map contributes no noise.</summary>
    private static string BuildWorkspaceMapSection(AppConfig config)
    {
        var topology = config.Topology;
        if (topology.Nodes.Count == 0)
            return "";

        var nodeById = topology.Nodes.ToDictionary(n => n.Id);

        var areaParts = topology.Nodes
            .GroupBy(n => n.Area)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var shown = g.Take(MaxMapNodes).Select(n => $"{n.DisplayLabel} ({n.ResourceKey})");
                var overflow = g.Count() - MaxMapNodes;
                return $"{MapAreaLabels[g.Key]}: {string.Join(", ", shown)}"
                    + (overflow > 0 ? $" (+{overflow} more)" : "");
            });

        var edgeParts = topology.Relationships
            .Where(r => nodeById.ContainsKey(r.FromNodeId) && nodeById.ContainsKey(r.ToNodeId))
            .Select(r =>
            {
                var text = $"{nodeById[r.FromNodeId].DisplayLabel} → {nodeById[r.ToNodeId].DisplayLabel}";
                return string.IsNullOrWhiteSpace(r.Label) ? text : $"{text} ({r.Label})";
            })
            .ToList();
        var edges = edgeParts.Take(MaxMapEdges).ToList();
        var edgeOverflow = edgeParts.Count - edges.Count;
        if (edgeOverflow > 0)
            edges.Add($"(+{edgeOverflow} more)");

        return $"""

            ## Workspace map (user-declared)
            Resources: {string.Join(" | ", areaParts)}
            Relationships: {(edges.Count > 0 ? string.Join(" · ", edges) : "(none declared yet)")}
            These relationships are declared by the user, not inferred — treat them as facts when reasoning across areas.
            """;
    }

    /// <summary>agent-correlation Module 2 — when a contextual turn's "feature" scope fences off
    /// configured areas, names them so the agent can point the user at the workspace-scope control
    /// instead of calling a tool it was never offered (which surfaces as an opaque "not available
    /// in this context" bridge error). Empty whenever no fence actually applies: no tool calling,
    /// no context area (the global page sees every area), workspace scope, or nothing else
    /// configured. Observability is deliberately not listed — its tools are exempt from the
    /// per-area filter (see <c>AgentToolCallOrchestrator.ResolveTools</c>), so it's never fenced.</summary>
    private static string BuildFencedAreasSection(
        ProfileData data, AppConfig config, AgentChatContext? context, string normalizedScope, bool hasToolCalling)
    {
        if (!hasToolCalling || normalizedScope != "feature")
            return "";

        if (context?.FeatureArea is not { Length: > 0 } areaName
            || !Enum.TryParse<FeatureArea>(areaName, ignoreCase: true, out var visibleArea))
            return "";

        var configured = new List<(FeatureArea Area, string Label)>();
        if (config.AksConfig is not null)
            configured.Add((FeatureArea.Aks, "Kubernetes"));
        if (data.ServiceBusNamespaces.Count > 0)
            configured.Add((FeatureArea.ServiceBus,
                $"Service Bus ({string.Join(", ", data.ServiceBusNamespaces.Select(n => n.Alias))})"));
        if (config.RedisConfig is { Caches.Count: > 0 } redis)
            configured.Add((FeatureArea.Redis, $"Redis ({redis.Caches.Count} cache(s))"));
        if (config.StorageAccounts.Count > 0)
            configured.Add((FeatureArea.Storage, $"Storage ({config.StorageAccounts.Count} account(s))"));

        var fenced = configured.Where(c => c.Area != visibleArea).Select(c => c.Label).ToList();
        if (fenced.Count == 0)
            return "";

        return $"""

            ## Other configured areas
            {string.Join(", ", fenced)} are configured in this workspace, but their tools are outside this turn's scope. If evidence points to one of them, tell the user to enable "Search across my whole workspace" and ask again — do not guess about resources you cannot inspect.

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
