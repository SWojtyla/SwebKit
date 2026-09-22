using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Sidecar.Services;

/// <summary>Pushed once a background proactive investigation completes — workspace-intelligence
/// Module 4. <see cref="RuleId"/>+<see cref="FiredAt"/> together are the same composite identity the
/// originating <see cref="AlertFiredEvent"/> has, so the frontend can de-dup a dismissed insight
/// against the firing event it came from. <see cref="Evidence"/> carries the investigation's
/// factual findings (agent-workspace-awareness Module 2) — null/empty for the legacy single-shot
/// fallback path, so older frontends simply render nothing extra.</summary>
public sealed record ProactiveInsightReadyEvent(
    string RuleId,
    DateTimeOffset FiredAt,
    string RuleName,
    string Summary,
    string SessionId,
    IReadOnlyList<string>? Evidence = null);

public enum ProactiveInsightStage { Started, Skipped, Failed }

/// <summary>Lifecycle event for a background proactive investigation — without it every
/// early-return gate in <see cref="ProactiveInsightService"/> was a silent drop, so a fired
/// alert could produce no insight and no explanation anywhere in the UI. <see cref="Reason"/>
/// carries the human-readable cause for <see cref="ProactiveInsightStage.Skipped"/> and
/// <see cref="ProactiveInsightStage.Failed"/>.</summary>
public sealed record ProactiveInsightStatusEvent(
    string RuleId,
    DateTimeOffset FiredAt,
    string RuleName,
    ProactiveInsightStage Stage,
    string? Reason = null);

/// <summary>
/// Subscribes to <see cref="MonitoringAlertEvaluationService.AlertFired"/> (workspace-intelligence
/// Module 4) and, when a fired rule's resource maps to a node in the user-curated workspace
/// topology, kicks off a fire-and-forget background investigation via
/// <see cref="ProactiveInvestigationRunner"/> — a bounded model-driven loop (agent-workspace-
/// awareness Module 2). If the runner can't produce a result it falls back to the original
/// single-shot <c>investigate_workspace_issue</c> call + one-line summary. Never blocks alert evaluation: <see cref="OnAlertFired"/> only schedules a
/// <see cref="Task.Run(Func{Task})"/> and returns immediately, and the whole thing fails silently
/// (logged, not thrown) if anything goes wrong — a broken proactive-insight pipeline must never take
/// the alert engine down with it.
///
/// Global rate limit (separate from each rule's own per-rule cooldown, which
/// <see cref="MonitoringAlertEvaluationService"/> already enforces): at most one investigation in
/// flight at a time, via a simple <see cref="Interlocked.CompareExchange(ref int, int, int)"/> flag —
/// a real incident can fire several different rules within seconds, and without this, that becomes a
/// burst of simultaneous LLM calls. Extras are dropped (not queued) — the simpler of the two options
/// the plan allowed, since a queued backlog of stale investigations for an incident that's already
/// evolved past them isn't obviously more useful than just waiting for the next one.
/// </summary>
public sealed class ProactiveInsightService
{
    private readonly IAlertRuleRepository _rules;
    private readonly ProfileRepository _profiles;
    private readonly IAgentToolRegistry _toolRegistry;
    private readonly IAgentModelClient _modelClient;
    private readonly UserSettingsRepository _settings;
    private readonly SidecarAgentChatService _chatService;
    private readonly ProactiveInvestigationRunner _investigationRunner;
    private readonly ILogger<ProactiveInsightService> _logger;
    private int _busy;

    public event Action<ProactiveInsightReadyEvent>? InsightReady;

    /// <summary>Raised for every investigation outcome other than success: <c>Started</c> when
    /// the runner kicks off, <c>Skipped</c> when a gate rejects it (with the reason), and
    /// <c>Failed</c> when it errors out. Streamed to the UI so an alert that produces no insight
    /// still produces an explanation.</summary>
    public event Action<ProactiveInsightStatusEvent>? InsightStatus;

    public ProactiveInsightService(
        MonitoringAlertEvaluationService engine,
        IAlertRuleRepository rules,
        ProfileRepository profiles,
        IAgentToolRegistry toolRegistry,
        IAgentModelClient modelClient,
        UserSettingsRepository settings,
        SidecarAgentChatService chatService,
        ProactiveInvestigationRunner investigationRunner,
        ILogger<ProactiveInsightService> logger)
    {
        _rules = rules;
        _profiles = profiles;
        _toolRegistry = toolRegistry;
        _modelClient = modelClient;
        _settings = settings;
        _chatService = chatService;
        _investigationRunner = investigationRunner;
        _logger = logger;

        engine.AlertFired += OnAlertFired;
    }

    private void OnAlertFired(AlertFiredEvent evt)
    {
        // Fire-and-forget on purpose: AlertFired is invoked synchronously from inside the
        // evaluation loop (see MonitoringAlertEvaluationService), so awaiting here would delay
        // every other rule's evaluation behind an LLM round trip.
        _ = Task.Run(() => HandleAlertFiredAsync(evt));
    }

    private void RaiseStatus(AlertFiredEvent evt, ProactiveInsightStage stage, string? reason = null)
    {
        try
        {
            InsightStatus?.Invoke(new ProactiveInsightStatusEvent(evt.RuleId, evt.FiredAt, evt.RuleName, stage, reason));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "InsightStatus handler threw for rule {RuleId}", evt.RuleId);
        }
    }

    private async Task HandleAlertFiredAsync(AlertFiredEvent evt)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            _logger.LogInformation(
                "Dropped proactive insight for rule {RuleId} ({RuleName}) — another investigation is already in flight.",
                evt.RuleId, evt.RuleName);
            RaiseStatus(evt, ProactiveInsightStage.Skipped, "another investigation is already in flight");
            return;
        }

        try
        {
            var hasToolCalling = (_settings.Settings.Agent.GetActiveProfile()?.Capability ?? AgentCapability.Unknown) >= AgentCapability.ToolCalling;
            if (!hasToolCalling)
            {
                RaiseStatus(evt, ProactiveInsightStage.Skipped, "no agent profile with tool calling is configured");
                return; // Module 7: nothing a tool-less model could usefully investigate with
            }

            var rule = await _rules.GetByIdAsync(evt.RuleId);
            if (rule is null)
            {
                RaiseStatus(evt, ProactiveInsightStage.Skipped, "the rule was deleted before the investigation started");
                return; // rule was deleted between firing and now — nothing to correlate against
            }

            if (!rule.AiInvestigationEnabled)
            {
                _logger.LogInformation(
                    "Skipped proactive insight for rule {RuleId} ({RuleName}) — AI investigation is disabled on this rule.",
                    evt.RuleId, evt.RuleName);
                RaiseStatus(evt, ProactiveInsightStage.Skipped, "AI investigation is disabled on this rule");
                return;
            }

            var start = FindStartingResource(rule);
            if (start is null)
            {
                RaiseStatus(evt, ProactiveInsightStage.Skipped, "this alert source can't be mapped to a workspace resource");
                return; // this rule's source type isn't one we know how to map to a topology node
            }

            // A fired resource that isn't on any map no longer gates the investigation — the
            // model has workspace-scope tools and can correlate on its own. The map only scopes
            // *which* declared relationships it sees (the map containing the resource, auto-matched
            // across every map the profile carries).
            var config = _profiles.Config;
            var effectiveContext = start.Value.Context
                ?? (start.Value.Area == WorkspaceResourceArea.Aks ? config.AksConfig?.KubeconfigContext : null);
            var match = WorkspaceMapLookup.FindNode(config.EffectiveMaps(), start.Value.Area, start.Value.Hint, effectiveContext);

            RaiseStatus(evt, ProactiveInsightStage.Started);

            // agent-workspace-awareness Module 2: prefer the bounded model-driven investigation
            // (the model picks its own read-only evidence path across the workspace). When it
            // can't produce a result — budget exceeded, empty response — fall back to the Module 4
            // single-shot topology walk + one-line summary so an alert still yields an insight.
            ProactiveInvestigationResult? result = null;
            try
            {
                result = await _investigationRunner.InvestigateAsync(
                    evt, DescribeStart(start.Value, effectiveContext), match?.Map, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Model-driven investigation for rule {RuleId} ({RuleName}) failed — falling back to the single-shot path.",
                    evt.RuleId, evt.RuleName);
            }

            string summary;
            string reportJson;
            IReadOnlyList<string>? evidence = null;
            if (result is not null)
            {
                summary = result.Hypothesis;
                reportJson = BuildReportJson(result);
                evidence = result.Evidence;
            }
            else
            {
                reportJson = await _toolRegistry.ExecuteAsync(
                    "investigate_workspace_issue",
                    BuildArgs(new
                    {
                        area = start.Value.Area.ToString(),
                        resource_hint = start.Value.Hint,
                        context = effectiveContext,
                        map_id = match?.Map.Id,
                    }),
                    CancellationToken.None);

                summary = await SummarizeAsync(evt, reportJson) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(summary))
                {
                    RaiseStatus(evt, ProactiveInsightStage.Failed, "the investigation produced no summary");
                    return; // summarization failed — a missing insight is fine, a garbled one is not
                }
            }

            var sessionId = $"proactive-{evt.RuleId}-{evt.FiredAt.ToUnixTimeMilliseconds()}";
            _chatService.SeedProactiveInsightSession(sessionId, evt.RuleName, evt.Message, reportJson, summary);

            InsightReady?.Invoke(new ProactiveInsightReadyEvent(evt.RuleId, evt.FiredAt, evt.RuleName, summary, sessionId, evidence));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Proactive insight investigation failed for rule {RuleId} ({RuleName})", evt.RuleId, evt.RuleName);
            RaiseStatus(evt, ProactiveInsightStage.Failed, ex.Message);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private async Task<string?> SummarizeAsync(AlertFiredEvent evt, string reportJson)
    {
        try
        {
            var request = new AgentModelRequest
            {
                SystemPrompt = "You produce a single short sentence (under 30 words) hypothesizing why a "
                    + "monitoring alert might be related to other workspace resources, based on a JSON "
                    + "investigation report. Do not add any preamble, formatting, or commentary — output "
                    + "only the one sentence.",
                UserMessage = $"Alert '{evt.RuleName}' fired: {evt.Message}\n\nInvestigation report:\n{reportJson}",
            };
            var response = await _modelClient.CompleteAsync(request, CancellationToken.None);
            return response.Content;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Renders the starting resource for the runner's user message — "Aks/prod" for a
    /// context-less match, "Aks/aks-dev/dev-briocomp" when a kubeconfig context pins the cluster,
    /// so the model knows which cluster's tools to point at.</summary>
    private static string DescribeStart(
        (WorkspaceResourceArea Area, string Hint, string? Context) start, string? effectiveContext) =>
        string.IsNullOrWhiteSpace(effectiveContext)
            ? $"{start.Area}/{start.Hint}"
            : $"{start.Area}/{effectiveContext}/{start.Hint}";

    /// <summary>Maps a fired rule's own params to the same (area, hint, context) shape
    /// <c>InvestigateWorkspaceIssueTool</c> expects — the rule doesn't carry an internal
    /// <c>WorkspaceResourceNode</c> id any more than the model does, so this is the same
    /// hint-matching approach, not a different mechanism. <c>Context</c> is the rule's pinned
    /// kubeconfig context for AKS sources (null for everything else and for rules that follow
    /// the globally configured context).</summary>
    private static (WorkspaceResourceArea Area, string Hint, string? Context)? FindStartingResource(MonitoringAlertRule rule) => rule.Source switch
    {
        AlertRuleSource.AksPodHealth or AlertRuleSource.AksPodRestartRate or AlertRuleSource.AksNamespaceHealthScore =>
            string.IsNullOrWhiteSpace(rule.AksPodParams?.Namespace)
                ? null
                : (WorkspaceResourceArea.Aks, rule.AksPodParams.Namespace,
                    string.IsNullOrWhiteSpace(rule.AksPodParams.KubeconfigContext) ? null : rule.AksPodParams.KubeconfigContext),

        AlertRuleSource.ServiceBusDlqDepth or AlertRuleSource.ServiceBusActiveDepth or AlertRuleSource.ServiceBusDeadSubscription =>
            string.IsNullOrWhiteSpace(rule.ServiceBusParams?.EntityPath) ? null : (WorkspaceResourceArea.ServiceBus, rule.ServiceBusParams.EntityPath, null),

        AlertRuleSource.RedisMemoryUsage or AlertRuleSource.RedisConnectedClients =>
            string.IsNullOrWhiteSpace(rule.RedisAlertParams?.ConnectionAlias) ? null : (WorkspaceResourceArea.Redis, rule.RedisAlertParams.ConnectionAlias, null),

        AlertRuleSource.StorageBlobCount =>
            string.IsNullOrWhiteSpace(rule.StorageParams?.AccountAlias) ? null : (WorkspaceResourceArea.Storage, rule.StorageParams.AccountAlias, null),

        _ => null,
    };

    /// <summary>Serializes a <see cref="ProactiveInvestigationResult"/> into the report JSON the
    /// seeded session carries — the structured fields plus the tool audit trail, but not
    /// <see cref="ProactiveInvestigationResult.RawText"/> (the same content in raw form, which the
    /// parsed fields already cover).</summary>
    private static string BuildReportJson(ProactiveInvestigationResult result) =>
        JsonSerializer.Serialize(new
        {
            hypothesis = result.Hypothesis,
            evidence = result.Evidence,
            severity = result.Severity,
            suggested_next_steps = result.SuggestedNextSteps,
            tools_used = result.ToolsUsed,
            hit_max_rounds = result.HitMaxRounds,
        });

    private static JsonElement BuildArgs(object obj) => JsonSerializer.SerializeToDocument(obj).RootElement;
}
