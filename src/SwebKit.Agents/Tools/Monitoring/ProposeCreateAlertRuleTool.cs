using System.Text.Json;
using SwebKit.Core.Models;

namespace SwebKit.Agents.Tools.Monitoring;

/// <summary>
/// Proposes creating a monitoring alert rule. Never creates directly — registers a
/// <see cref="PendingAgentAction"/> for user confirmation, same propose-only pattern as the
/// other mutate tools; the confirmed action is applied by the sidecar's
/// <c>MonitoringActionExecutor</c> (which also reloads the evaluation engine).
///
/// Params are flat (<c>aks_namespace</c>, <c>servicebus_entity_path</c>, …) rather than the
/// rule model's nested param bags — an LLM emits one flat object far more reliably than it
/// picks the right bag shape per source. The executor maps them onto the correct bag.
/// </summary>
public sealed class ProposeCreateAlertRuleTool : IAgentTool
{
    private readonly IAgentActionCoordinator _coordinator;

    public ProposeCreateAlertRuleTool(IAgentActionCoordinator coordinator) => _coordinator = coordinator;

    public string Name => "propose_create_alert_rule";
    public string Description =>
        "Propose creating a monitoring alert rule that watches a resource and fires when its " +
        "condition is met (e.g. pod unhealthy, DLQ depth above threshold). Returns a pending " +
        "action for user confirmation — the rule is not created until confirmed. When " +
        "ai_investigation_enabled is true (the default), a firing also triggers a background " +
        "AI investigation of related workspace resources.";
    public FeatureArea FeatureArea => FeatureArea.Monitoring;
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.Low;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "Display name for the rule." },
            "source": {
              "type": "string",
              "description": "What the rule watches.",
              "enum": ["AksPodHealth", "AksPodRestartRate", "AksNamespaceHealthScore",
                       "ServiceBusDlqDepth", "ServiceBusActiveDepth", "ServiceBusDeadSubscription",
                       "RedisMemoryUsage", "RedisConnectedClients", "StorageBlobCount"]
            },
            "severity": { "type": "string", "enum": ["Warning", "Critical"], "description": "Default Warning." },
            "interval_seconds": { "type": "integer", "description": "Evaluation interval. Default 60." },
            "cooldown_minutes": { "type": "integer", "description": "Minimum minutes between firings. Default 5." },
            "ai_investigation_enabled": { "type": "boolean", "description": "Run a background AI investigation when this rule fires. Default true." },
            "aks_namespace": { "type": "string", "description": "Required for Aks* sources: the Kubernetes namespace to watch." },
            "aks_restart_threshold": { "type": "integer", "description": "AksPodRestartRate: restarts within the interval that count as firing. Default 5." },
            "aks_health_score_threshold": { "type": "number", "description": "Aks* health sources: unhealthy fraction that counts as firing. Default 0.25." },
            "servicebus_connection_alias": { "type": "string", "description": "ServiceBus* sources: configured namespace alias. Empty = first configured." },
            "servicebus_entity_path": { "type": "string", "description": "Required for ServiceBus* sources: queue or topic/subscription path." },
            "servicebus_message_count_threshold": { "type": "integer", "description": "ServiceBus* depth sources: message count that counts as firing. Default 1." },
            "redis_connection_alias": { "type": "string", "description": "Redis* sources: configured cache alias. Empty = first configured." },
            "redis_memory_threshold_percent": { "type": "number", "description": "RedisMemoryUsage: percent that counts as firing. Default 80." },
            "redis_client_count_lower_bound": { "type": "integer", "description": "RedisConnectedClients: client count that counts as firing. Default 1." },
            "storage_account_alias": { "type": "string", "description": "StorageBlobCount: configured account alias. Empty = first configured." },
            "storage_container_name": { "type": "string", "description": "Required for StorageBlobCount: container to count blobs in." },
            "storage_blob_count_threshold": { "type": "integer", "description": "StorageBlobCount: blob count that counts as firing. Default 1000." }
          },
          "required": ["name", "source"]
        }
        """);

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("name", out var nameEl) || nameEl.GetString() is not { Length: > 0 } name)
            return Task.FromResult("""{"error":"Missing required parameter 'name'."}""");
        if (!arguments.TryGetProperty("source", out var sourceEl) || sourceEl.GetString() is not { Length: > 0 } sourceName
            || !Enum.TryParse<AlertRuleSource>(sourceName, ignoreCase: true, out var source))
            return Task.FromResult("""{"error":"Missing or invalid required parameter 'source'."}""");

        // Cheap source-shape validation so the preview doesn't propose a rule that could never
        // evaluate — the executor re-validates at apply time.
        var missing = MissingRequiredParam(source, arguments);
        if (missing is not null)
            return Task.FromResult(JsonSerializer.Serialize(new { error = $"Source '{sourceName}' requires '{missing}'." }));

        var actionId = Guid.NewGuid().ToString("N");
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.CreateAlertRule,
            Summary = $"Create alert rule '{name}' ({sourceName})",
            Target = $"Monitoring rule '{name}'",
            Risk = AgentActionRisk.Low,
            Preview = BuildPreview(name, sourceName, arguments),
            ExpectedFingerprint = null,
            Payload = arguments.Clone(),
        };
        _coordinator.RegisterAction(action);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            action_id = actionId,
            status = "pending_confirmation",
            summary = action.Summary,
            preview = action.Preview,
            risk = "Low",
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "Alert rule proposed. User must explicitly confirm before it is created.",
        }));
    }

    /// <summary>The one parameter each source genuinely cannot evaluate without — everything
    /// else has a sensible default either here or in the param-bag models.</summary>
    private static string? MissingRequiredParam(AlertRuleSource source, JsonElement args) => source switch
    {
        AlertRuleSource.AksPodHealth or AlertRuleSource.AksPodRestartRate or AlertRuleSource.AksNamespaceHealthScore
            when !Has(args, "aks_namespace") => "aks_namespace",
        AlertRuleSource.ServiceBusDlqDepth or AlertRuleSource.ServiceBusActiveDepth or AlertRuleSource.ServiceBusDeadSubscription
            when !Has(args, "servicebus_entity_path") => "servicebus_entity_path",
        AlertRuleSource.StorageBlobCount
            when !Has(args, "storage_container_name") => "storage_container_name",
        _ => null,
    };

    private static bool Has(JsonElement args, string prop) =>
        args.TryGetProperty(prop, out var el) && el.GetString() is { Length: > 0 };

    private static string BuildPreview(string name, string source, JsonElement args)
    {
        var lines = new List<string> { $"Name: {name}", $"Source: {source}" };
        foreach (var prop in args.EnumerateObject())
        {
            if (prop.Name is "name" or "source") continue;
            lines.Add($"{prop.Name}: {prop.Value}");
        }
        lines.Add("");
        lines.Add("The rule evaluates on its interval once created. With AI investigation enabled,");
        lines.Add("a firing triggers a background investigation of related workspace resources.");
        return string.Join('\n', lines);
    }
}
