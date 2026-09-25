using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Services;

/// <summary>Structured output of one background proactive investigation (agent-workspace-awareness
/// Module 2). <see cref="Evidence"/> is the model's factual findings; <see cref="ToolsUsed"/> is the
/// audit trail of which tools the loop actually called — both end up in the seeded chat session so
/// the user can see *how* the hypothesis was reached, not just the conclusion.</summary>
public sealed record ProactiveInvestigationResult(
    string Hypothesis,
    IReadOnlyList<string> Evidence,
    string? Severity,
    IReadOnlyList<string> SuggestedNextSteps,
    ProposedFix? ProposedFix,
    string RawText,
    IReadOnlyList<string> ToolsUsed,
    bool HitMaxRounds);

/// <summary>
/// Runs one bounded, model-driven investigation for a fired alert — the Module 2 replacement for
/// the single fixed <c>investigate_workspace_issue</c> call the pipeline used before. The model
/// chooses its own evidence path across the whole workspace (which tools, in what order) instead
/// of running one predetermined topology walk.
///
/// Bounds, deliberately mirroring the chat pipeline's own limits:
/// <list type="bullet">
/// <item><b>Read-only:</b> tools are resolved at ask mode — <c>propose_*</c> mutations are filtered
/// out by the mode gate, so nothing mutating is reachable in an unsupervised run.</item>
/// <item><b>Workspace scope:</b> every configured area's tools are visible (no context fence), the
/// same visibility the "search across my whole workspace" escalation grants a user turn.</item>
/// <item><b>Round + wall-clock caps:</b> <c>AgentModelRequest.MaxToolRounds</c> (same 5 the chat
/// clients use) plus a hard wall-clock cancellation (90s default) — a slow or looping
/// investigation gives up rather than burning the provider.</item>
/// </list>
///
/// Output contract: the model is asked to end with a JSON object
/// (hypothesis/evidence/severity/suggested_next_steps). When it doesn't comply, the raw text is
/// still returned as the hypothesis — a garbled-but-present insight beats a silently missing one
/// only when the text is usable; unparseable-empty responses return null so the caller can fall
/// back to the old single-shot path.
/// </summary>
public sealed class ProactiveInvestigationRunner
{
    private static readonly TimeSpan DefaultInvestigationBudget = TimeSpan.FromSeconds(90);

    /// <summary>Same cap the chat clients use internally (AgentModelRequest.MaxToolRounds default).</summary>
    private const int MaxToolRounds = 5;

    private const string InvestigationInstructions = """

        ## Background investigation mode
        A monitoring alert just fired. The user is NOT watching this conversation — you are
        investigating on their behalf, and your final reply is stored as a report they read later.
        Use the available tools to gather evidence across the workspace: start from the resource
        the alert names, then follow the declared workspace map relationships to check neighboring
        resources (the map is already in context above). Prefer a few high-signal tool calls over
        exhaustive enumeration.

        When you have enough evidence, respond with ONLY a JSON object in this exact shape:
        {
          "hypothesis": "one-sentence root-cause hypothesis",
          "evidence": ["short factual findings taken from the tool results"],
          "severity": "low" | "medium" | "high",
          "suggested_next_steps": ["concrete actions the user could take, most actionable first"],
          "proposed_fix": {
            "explanation": "one line describing the change",
            "language": "yaml" | "json" | "env" | "text",
            "snippet": "the minimal corrected configuration, ready to apply"
          }
        }
        Set "proposed_fix" to null when the root cause is not a concrete misconfiguration (e.g. a
        resource or external dependency is simply down). When it is — a bad hostname, a wrong env
        var, a malformed connection string, a wrong image tag — always include the corrected value
        itself, not just a description of what to change.
        For every workspace resource you inspected beyond the alerting one, include an evidence
        entry saying whether it is implicated in or ruled out of the root cause.
        No prose, no markdown fences — the JSON object only.
        """;

    /// <summary>The map-less instructions variant: no declared map covers the fired resource, so
    /// there are no relationships to follow — the model has to find likely neighbors from live
    /// data instead. An alert must still yield an investigation even when the user never mapped
    /// the resource.</summary>
    private const string InvestigationInstructionsNoMap = """

        ## Background investigation mode
        A monitoring alert just fired. The user is NOT watching this conversation — you are
        investigating on their behalf. Use the available tools to gather evidence across the
        workspace: start from the resource the alert names and inspect it directly. No workspace
        map covers this resource, so there are no declared relationships to follow — look for
        likely neighbors yourself (same-namespace workloads, queues or caches a failing resource
        would plausibly depend on, observability telemetry for the same timeframe). Prefer a few
        high-signal tool calls over exhaustive enumeration.

        When you have enough evidence, respond with ONLY a JSON object in this exact shape:
        {
          "hypothesis": "one-sentence root-cause hypothesis",
          "evidence": ["short factual findings taken from the tool results"],
          "severity": "low" | "medium" | "high",
          "suggested_next_steps": ["concrete actions the user could take, most actionable first"],
          "proposed_fix": {
            "explanation": "one line describing the change",
            "language": "yaml" | "json" | "env" | "text",
            "snippet": "the minimal corrected configuration, ready to apply"
          }
        }
        Set "proposed_fix" to null when the root cause is not a concrete misconfiguration (e.g. a
        resource or external dependency is simply down). When it is — a bad hostname, a wrong env
        var, a malformed connection string, a wrong image tag — always include the corrected value
        itself, not just a description of what to change.
        For every workspace resource you inspected beyond the alerting one, include an evidence
        entry saying whether it is implicated in or ruled out of the root cause.
        No prose, no markdown fences — the JSON object only.
        """;

    private readonly IAgentModelClient _modelClient;
    private readonly AgentToolCallOrchestrator _toolOrchestrator;
    private readonly AgentSystemPromptBuilder _promptBuilder;
    private readonly ILogger<ProactiveInvestigationRunner> _logger;
    private readonly TimeSpan _budget;

    /// <summary>Builds its orchestrator + prompt builder from the same primitives
    /// <see cref="SidecarAgentChatService"/>'s convenience constructor uses — those collaborators
    /// aren't DI-registered individually, so constructing them here keeps the composition pattern.
    /// <paramref name="budget"/> overrides the wall-clock cap (test seam — production uses the
    /// 90-second default).</summary>
    public ProactiveInvestigationRunner(
        IAgentModelClient modelClient,
        IAgentToolRegistry toolRegistry,
        ProfileRepository profiles,
        DemoModeService demo,
        ILogger<ProactiveInvestigationRunner> logger,
        TimeSpan? budget = null)
    {
        _modelClient = modelClient;
        _toolOrchestrator = new AgentToolCallOrchestrator(toolRegistry);
        _promptBuilder = new AgentSystemPromptBuilder(profiles, demo);
        _logger = logger;
        _budget = budget ?? DefaultInvestigationBudget;
    }

    /// <summary>Runs the bounded loop and parses the structured output. Returns null when the
    /// investigation could not produce anything usable (no tools resolved, budget exceeded, model
    /// returned nothing) — the caller decides whether to fall back or drop the insight.
    /// <paramref name="map"/> scopes the prompt's workspace-map section to the one map the fired
    /// resource matched (auto-match by resource — other projects' maps stay out of context);
    /// <c>null</c> means nothing matched, which switches the instructions to map-less
    /// self-discovery rather than skipping the investigation.</summary>
    public async Task<ProactiveInvestigationResult?> InvestigateAsync(
        AlertFiredEvent evt,
        string startingResourceHint,
        WorkspaceMap? map,
        CancellationToken ct)
    {
        var tools = _toolOrchestrator.ResolveTools(
            hasToolCalling: true,
            normalizedMode: AgentToolCallOrchestrator.NormalizeMode(null),
            context: null,
            normalizedScope: AgentToolCallOrchestrator.WorkspaceScope);
        if (tools.Count == 0)
        {
            _logger.LogWarning("Proactive investigation for rule {RuleId} resolved zero tools — nothing to investigate with.", evt.RuleId);
            return null;
        }

        var systemPrompt =
            _promptBuilder.Build(null, AgentToolCallOrchestrator.NormalizeMode(null), AgentToolCallOrchestrator.WorkspaceScope, hasToolCalling: true,
                maps: map is null ? [] : [map], forBackgroundInvestigation: true)
            + (map is null ? InvestigationInstructionsNoMap : InvestigationInstructions);

        var steps = new List<AgentChatStep>();
        var toolExecutor = _toolOrchestrator.BuildStepTrackingToolExecutor(tools, steps);

        var request = new AgentModelRequest
        {
            SystemPrompt = systemPrompt,
            UserMessage = BuildUserMessage(evt, startingResourceHint),
            Tools = tools,
            MaxToolRounds = MaxToolRounds,
        };

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(_budget);

        AgentChatResult result;
        try
        {
            result = await _modelClient.ChatAsync(request, toolExecutor, budget.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Proactive investigation for rule {RuleId} ({RuleName}) hit the {Seconds}s budget — {Tools} tool calls made before cancellation.",
                evt.RuleId, evt.RuleName, (int)_budget.TotalSeconds,
                steps.Count(s => s.Type == "tool_call"));
            return null;
        }

        if (string.IsNullOrWhiteSpace(result.Text))
            return null;

        return ParseResult(result, steps);
    }

    private static string BuildUserMessage(AlertFiredEvent evt, string startingResourceHint) =>
        $"Alert fired: \"{evt.RuleName}\" (severity: {evt.Severity})\n" +
        $"Message: {evt.Message}\n" +
        $"Detail: {evt.Detail}\n" +
        $"Start the investigation from the workspace resource matching: {startingResourceHint}.";

    /// <summary>Extracts the structured JSON the instructions demand. The model is asked for
    /// JSON-only output, but providers sometimes wrap it in prose or fences — so parse the first
    /// balanced {...} block found rather than the whole text. On any parse failure the raw text
    /// becomes the hypothesis (truncated) with an empty evidence list — still a usable insight.</summary>
    private ProactiveInvestigationResult ParseResult(AgentChatResult result, List<AgentChatStep> steps)
    {
        var toolsUsed = result.ToolsUsed.Count > 0
            ? result.ToolsUsed
            : steps.Where(s => s.Type == "tool_call" && s.ToolName is not null).Select(s => s.ToolName!).Distinct().ToList();

        return ParseStructuredOutput(result.Text, toolsUsed, result.HitMaxRounds);
    }

    /// <summary>Parses the model's JSON-object output into a <see cref="ProactiveInvestigationResult"/>.
    /// Internal (not private) because <see cref="ProactiveInsightService"/>'s single-shot fallback
    /// asks the model for the same contract — it just drafts it from a precomputed probe instead
    /// of a live tool loop, so the parsed fields flow into the same report shape either way.
    /// A model that returns non-JSON prose still yields a usable result: the raw text becomes the
    /// hypothesis (truncated) with empty structured fields.</summary>
    internal ProactiveInvestigationResult ParseStructuredOutput(
        string text, IReadOnlyList<string> toolsUsed, bool hitMaxRounds)
    {
        text = text.Trim();
        var json = ExtractFirstJsonObject(text);
        if (json is null)
        {
            return new ProactiveInvestigationResult(
                Hypothesis: text.Length > 400 ? text[..400] : text,
                Evidence: [],
                Severity: null,
                SuggestedNextSteps: [],
                ProposedFix: null,
                RawText: text,
                ToolsUsed: toolsUsed,
                HitMaxRounds: hitMaxRounds);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new ProactiveInvestigationResult(
                Hypothesis: root.TryGetProperty("hypothesis", out var h) && h.ValueKind == JsonValueKind.String
                    ? h.GetString() ?? text
                    : text,
                Evidence: ReadStringArray(root, "evidence"),
                Severity: root.TryGetProperty("severity", out var s) && s.ValueKind == JsonValueKind.String
                    ? s.GetString()
                    : null,
                SuggestedNextSteps: ReadStringArray(root, "suggested_next_steps"),
                ProposedFix: ReadProposedFix(root),
                RawText: text,
                ToolsUsed: toolsUsed,
                HitMaxRounds: hitMaxRounds);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Proactive investigation output was not valid JSON — using raw text as hypothesis.");
            return new ProactiveInvestigationResult(
                Hypothesis: text.Length > 400 ? text[..400] : text,
                Evidence: [],
                Severity: null,
                SuggestedNextSteps: [],
                ProposedFix: null,
                RawText: text,
                ToolsUsed: toolsUsed,
                HitMaxRounds: hitMaxRounds);
        }
    }

    /// <summary>Reads the optional <c>proposed_fix</c> object. Tolerant of the model emitting a
    /// bare string instead of the {explanation, language, snippet} object — a fix described in
    /// the wrong shape still beats none. Returns null for null/missing/empty values.</summary>
    private static ProposedFix? ReadProposedFix(JsonElement root)
    {
        if (!root.TryGetProperty("proposed_fix", out var fix))
            return null;

        if (fix.ValueKind == JsonValueKind.String)
        {
            var snippet = fix.GetString();
            return string.IsNullOrWhiteSpace(snippet) ? null : new ProposedFix { Snippet = snippet };
        }

        if (fix.ValueKind != JsonValueKind.Object)
            return null;

        var snippetText = fix.TryGetProperty("snippet", out var sn) && sn.ValueKind == JsonValueKind.String
            ? sn.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(snippetText))
            return null;

        return new ProposedFix
        {
            Snippet = snippetText,
            Explanation = fix.TryGetProperty("explanation", out var ex) && ex.ValueKind == JsonValueKind.String
                ? ex.GetString() ?? string.Empty
                : string.Empty,
            Language = fix.TryGetProperty("language", out var lang) && lang.ValueKind == JsonValueKind.String
                ? lang.GetString() ?? "text"
                : "text",
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        return arr.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(10)
            .ToList();
    }

    /// <summary>Finds the first top-level {...} span in the text by brace matching — tolerant of
    /// leading prose ("Here is my finding: {...}") and trailing text. Returns null when no balanced
    /// object exists.</summary>
    private static string? ExtractFirstJsonObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0)
            return null;

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0)
                        return text[start..(i + 1)];
                    break;
            }
        }
        return null;
    }
}
