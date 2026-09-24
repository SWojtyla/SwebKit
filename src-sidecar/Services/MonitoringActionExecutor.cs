using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Applies confirmed <see cref="AgentActionType.CreateAlertRule"/> actions (agent-workspace-
/// awareness): maps the flat tool params onto a <see cref="MonitoringAlertRule"/>, persists it
/// through the same repository the REST endpoints use, then reloads the evaluation engine so the
/// new rule starts evaluating on its own interval — the same thing the POST /api/monitoring/rules
/// endpoint does after an upsert.
///
/// Lives in the sidecar (not SwebKit.Agents) because applying the action needs
/// <see cref="MonitoringAlertEvaluationService"/>, which is a sidecar service.
/// </summary>
public sealed class MonitoringActionExecutor : IAgentActionExecutor
{
    private readonly IAlertRuleRepository _rules;
    private readonly MonitoringAlertEvaluationService _engine;
    private readonly ILogger<MonitoringActionExecutor> _logger;

    public MonitoringActionExecutor(
        IAlertRuleRepository rules,
        MonitoringAlertEvaluationService engine,
        ILogger<MonitoringActionExecutor> logger)
    {
        _rules = rules;
        _engine = engine;
        _logger = logger;
    }

    public bool CanHandle(AgentActionType type) => type == AgentActionType.CreateAlertRule;

    public async Task<AgentActionResult> ApplyAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } args)
            return Fail("Action has no payload.");
        var name = args.TryGetProperty("name", out var n) ? n.GetString() : null;
        var sourceName = args.TryGetProperty("source", out var s) ? s.GetString() : null;
        if (string.IsNullOrWhiteSpace(name) || !Enum.TryParse<AlertRuleSource>(sourceName, ignoreCase: true, out var source))
            return Fail("Action payload is missing 'name' or a valid 'source'.");

        var rule = new MonitoringAlertRule
        {
            Name = name,
            Source = source,
            Severity = Enum.TryParse<AlertSeverity>(Get(args, "severity"), ignoreCase: true, out var sev) ? sev : AlertSeverity.Warning,
            IntervalSeconds = GetInt(args, "interval_seconds") ?? 60,
            CooldownMinutes = GetInt(args, "cooldown_minutes") ?? 5,
            AiInvestigationEnabled = GetBool(args, "ai_investigation_enabled") ?? true,
            AksPodParams = source is AlertRuleSource.AksPodHealth or AlertRuleSource.AksPodRestartRate or AlertRuleSource.AksNamespaceHealthScore
                ? new AksPodAlertParams
                {
                    KubeconfigContext = Get(args, "aks_context") ?? string.Empty,
                    Namespace = Get(args, "aks_namespace") ?? string.Empty,
                    RestartThreshold = GetInt(args, "aks_restart_threshold") ?? 5,
                    HealthScoreThreshold = GetDouble(args, "aks_health_score_threshold") ?? 0.25,
                }
                : null,
            ServiceBusParams = source is AlertRuleSource.ServiceBusDlqDepth or AlertRuleSource.ServiceBusActiveDepth or AlertRuleSource.ServiceBusDeadSubscription
                ? new ServiceBusAlertParams
                {
                    NamespaceConnectionAlias = Get(args, "servicebus_connection_alias") ?? string.Empty,
                    EntityPath = Get(args, "servicebus_entity_path") ?? string.Empty,
                    MessageCountThreshold = GetLong(args, "servicebus_message_count_threshold") ?? 1,
                }
                : null,
            RedisAlertParams = source is AlertRuleSource.RedisMemoryUsage or AlertRuleSource.RedisConnectedClients
                ? new RedisAlertParams
                {
                    ConnectionAlias = Get(args, "redis_connection_alias") ?? string.Empty,
                    MemoryUsageThresholdPercent = GetDouble(args, "redis_memory_threshold_percent") ?? 80.0,
                    ClientCountLowerBound = GetInt(args, "redis_client_count_lower_bound") ?? 1,
                }
                : null,
            StorageParams = source == AlertRuleSource.StorageBlobCount
                ? new StorageAlertParams
                {
                    AccountAlias = Get(args, "storage_account_alias") ?? string.Empty,
                    ContainerName = Get(args, "storage_container_name") ?? string.Empty,
                    BlobCountThreshold = GetLong(args, "storage_blob_count_threshold") ?? 1000,
                }
                : null,
        };

        await _rules.UpsertAsync(rule);
        await _engine.ReloadRulesAsync();
        _logger.LogInformation("Agent-created alert rule '{Name}' ({Source}) applied.", rule.Name, rule.Source);

        return new AgentActionResult
        {
            IsSuccess = true,
            ResultSummary = $"Alert rule '{rule.Name}' created and now evaluating every {rule.IntervalSeconds}s.",
        };
    }

    private static string? Get(JsonElement args, string prop) =>
        args.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static int? GetInt(JsonElement args, string prop) =>
        args.TryGetProperty(prop, out var el) && el.TryGetInt32(out var v) ? v : null;

    private static long? GetLong(JsonElement args, string prop) =>
        args.TryGetProperty(prop, out var el) && el.TryGetInt64(out var v) ? v : null;

    private static double? GetDouble(JsonElement args, string prop) =>
        args.TryGetProperty(prop, out var el) && el.TryGetDouble(out var v) ? v : null;

    private static bool? GetBool(JsonElement args, string prop) =>
        args.TryGetProperty(prop, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? el.GetBoolean()
            : null;

    private static AgentActionResult Fail(string message) => new() { IsSuccess = false, ErrorMessage = message };
}
