using System.Text.Json;
using SwebKit.Agents.Tools;

namespace SwebKit.Sidecar.Services;

/// <summary>Returns recent fired alerts from the monitoring engine's in-memory history. Lives in the
/// sidecar (not SwebKit.Agents) because the history ring buffer is held by the sidecar-hosted
/// <see cref="MonitoringAlertEvaluationService"/> — same reason <see cref="MonitoringActionExecutor"/>
/// lives here. Read-only: lets the agent answer "what else fired recently?" during an investigation,
/// which is the signal that distinguishes a single failure from an alert storm.</summary>
public sealed class GetAlertHistoryTool : IAgentTool
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 50;

    private readonly MonitoringAlertEvaluationService _engine;

    public GetAlertHistoryTool(MonitoringAlertEvaluationService engine) => _engine = engine;

    public string Name => "get_alert_history";

    public string Description =>
        "Returns recently fired monitoring alerts, newest first, with rule name, source, severity, " +
        "message, and fire time. Use to correlate a firing alert with other recent alerts — e.g. " +
        "whether related resources also fired (alert storm) or this rule is flapping.";

    public FeatureArea FeatureArea => FeatureArea.Monitoring;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "rule_id": { "type": "string", "description": "Optional rule id to filter history to a single rule." },
            "limit": { "type": "integer", "description": "Max alerts to return, newest first. Defaults to 20, capped at 50." }
          },
          "required": []
        }
        """);

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var ruleId = arguments.TryGetProperty("rule_id", out var r) ? r.GetString() : null;
        var limit = DefaultLimit;
        if (arguments.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out var lv) && lv > 0)
            limit = Math.Min(lv, MaxLimit);

        var alerts = _engine.RecentAlerts
            .Where(a => ruleId is null || a.RuleId == ruleId)
            .OrderByDescending(a => a.FiredAt)
            .Take(limit)
            .Select(a => new
            {
                rule_id = a.RuleId,
                rule_name = a.RuleName,
                source = a.Source.ToString(),
                severity = a.Severity.ToString(),
                message = a.Message,
                detail = a.Detail,
                fired_at = a.FiredAt,
                profile = a.ProfileName,
            })
            .ToList();

        return Task.FromResult(JsonSerializer.Serialize(new { alert_count = alerts.Count, alerts }));
    }
}
