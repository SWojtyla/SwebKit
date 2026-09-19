using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Agents.Tools.Monitoring;

/// <summary>Lists the configured monitoring alert rules. During a proactive investigation this lets
/// the agent see which other rules exist — e.g. whether sibling rules cover related resources that
/// might also be firing — without needing the Monitoring page open.</summary>
public sealed class ListAlertRulesTool : IAgentTool
{
    private const int MaxRules = 100;

    private readonly IAlertRuleRepository _rules;

    public ListAlertRulesTool(IAlertRuleRepository rules) => _rules = rules;

    public string Name => "list_alert_rules";

    public string Description =>
        "Lists the configured monitoring alert rules with their source, target, severity, AI-investigation " +
        "flag, and last evaluated/fired timestamps. Use to see what else is being watched — e.g. whether " +
        "related resources also have rules that may be firing.";

    public FeatureArea FeatureArea => FeatureArea.Monitoring;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "source": { "type": "string", "description": "Optional AlertRuleSource filter, e.g. 'AksPodHealth' or 'ServiceBusDlqDepth'." },
            "enabled_only": { "type": "boolean", "description": "When true, only return enabled rules. Defaults to false." }
          },
          "required": []
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var enabledOnly = arguments.TryGetProperty("enabled_only", out var eo) && eo.ValueKind == JsonValueKind.True;
        AlertRuleSource? sourceFilter = null;
        if (arguments.TryGetProperty("source", out var s) && s.GetString() is { Length: > 0 } sv)
        {
            if (!Enum.TryParse<AlertRuleSource>(sv, ignoreCase: true, out var parsed))
                return JsonSerializer.Serialize(new { error = $"Unknown source '{sv}'.", valid_sources = Enum.GetNames<AlertRuleSource>() });
            sourceFilter = parsed;
        }

        IReadOnlyList<MonitoringAlertRule> rules;
        try
        {
            rules = await _rules.GetAllAsync();
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }

        var filtered = rules
            .Where(r => !enabledOnly || r.Enabled)
            .Where(r => sourceFilter is null || r.Source == sourceFilter.Value)
            .Take(MaxRules)
            .Select(r => new
            {
                id = r.Id,
                name = r.Name,
                enabled = r.Enabled,
                source = r.Source.ToString(),
                severity = r.Severity.ToString(),
                interval_seconds = r.IntervalSeconds,
                ai_investigation_enabled = r.AiInvestigationEnabled,
                target = Target(r),
                last_evaluated_at = r.LastEvaluatedAt,
                last_fired_at = r.LastFiredAt,
            })
            .ToList();

        return JsonSerializer.Serialize(new { rule_count = filtered.Count, total_rules = rules.Count, rules = filtered });
    }

    private static string Target(MonitoringAlertRule r) => r.Source switch
    {
        AlertRuleSource.AksPodHealth or AlertRuleSource.AksPodRestartRate or AlertRuleSource.AksNamespaceHealthScore
            => r.AksPodParams is { } aks ? $"namespace:{aks.Namespace}" : "",
        AlertRuleSource.ServiceBusDlqDepth or AlertRuleSource.ServiceBusActiveDepth or AlertRuleSource.ServiceBusDeadSubscription
            => r.ServiceBusParams is { } sb ? sb.EntityPath : "",
        AlertRuleSource.RedisMemoryUsage or AlertRuleSource.RedisConnectedClients
            => r.RedisAlertParams?.ConnectionAlias ?? "",
        AlertRuleSource.StorageBlobCount
            => r.StorageParams is { } st ? $"{st.AccountAlias}/{st.ContainerName}" : "",
        _ => "",
    };
}
