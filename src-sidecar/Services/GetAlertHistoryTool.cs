using System.Text.Json;
using SwebKit.Agents.Tools;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Sidecar.Services;

/// <summary>Returns recent alert-history entries — the durable store merged with the engine's
/// in-memory ring buffer, same as the /api/monitoring/history endpoint, so the agent sees the
/// same incident record as the UI (firings survive restarts; suppressions and resolutions are
/// visible as their own <see cref="AlertHistoryKind"/> rows). Lives in the sidecar (not
/// SwebKit.Agents) because both sources are sidecar-hosted — same reason
/// <see cref="MonitoringActionExecutor"/> lives here. Read-only: lets the agent answer "what
/// else fired recently?" during an investigation, which is the signal that distinguishes a
/// single failure from an alert storm.</summary>
public sealed class GetAlertHistoryTool : IAgentTool
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 50;

    private readonly MonitoringAlertEvaluationService _engine;
    private readonly IAlertHistoryRepository _history;

    public GetAlertHistoryTool(MonitoringAlertEvaluationService engine, IAlertHistoryRepository history)
        => (_engine, _history) = (engine, history);

    public string Name => "get_alert_history";

    public string Description =>
        "Returns recent monitoring alert history, newest first — firings, silenced/suppressed " +
        "firings, and resolutions — with rule name, source, severity, kind, message, and time. " +
        "Use to correlate a firing alert with other recent alerts — e.g. whether related " +
        "resources also fired (alert storm), this rule is flapping, or it has since resolved.";

    public FeatureArea FeatureArea => FeatureArea.Monitoring;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "rule_id": { "type": "string", "description": "Optional rule id to filter history to a single rule." },
            "limit": { "type": "integer", "description": "Max entries to return, newest first. Defaults to 20, capped at 50." }
          },
          "required": []
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var ruleId = arguments.TryGetProperty("rule_id", out var r) ? r.GetString() : null;
        var limit = DefaultLimit;
        if (arguments.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out var lv) && lv > 0)
            limit = Math.Min(lv, MaxLimit);

        var persisted = await _history.GetAllAsync();
        var seen = new HashSet<string>(persisted.Select(Key));

        var entries = persisted
            .Select(e => (Entry: e, Detail: (string?)null, Profile: (string?)null))
            .Concat(_engine.RecentAlerts.Select(ToEntry).Where(e => seen.Add(Key(e.Entry))))
            .Where(e => ruleId is null || e.Entry.RuleId == ruleId)
            .OrderByDescending(e => e.Entry.At)
            .Take(limit)
            .Select(e => new
            {
                rule_id = e.Entry.RuleId,
                rule_name = e.Entry.RuleName,
                source = e.Entry.Source.ToString(),
                severity = e.Entry.Severity.ToString(),
                kind = e.Entry.Kind.ToString(),
                message = e.Entry.Message,
                detail = e.Detail,
                at = e.Entry.At,
                profile = e.Profile,
            })
            .ToList();

        return JsonSerializer.Serialize(new { alert_count = entries.Count, alerts = entries });

        static string Key(AlertHistoryEntry e) => $"{e.RuleId}|{e.At.UtcTicks}|{e.Kind}";

        // Ring-buffer rows still carry the firing's Detail/Profile context; durable entries
        // intentionally store less, so those fields serialize as null for them.
        static (AlertHistoryEntry Entry, string? Detail, string? Profile) ToEntry(AlertFiredEvent a) =>
            (new AlertHistoryEntry
            {
                RuleId = a.RuleId,
                RuleName = a.RuleName,
                Source = a.Source,
                Severity = a.Severity,
                Kind = a.Suppressed ? AlertHistoryKind.Suppressed : AlertHistoryKind.Fired,
                At = a.FiredAt,
                Message = a.SuppressedBy is { } by ? $"{a.Message} (silenced: {by})" : a.Message,
            }, a.Detail, a.ProfileName);
    }
}
