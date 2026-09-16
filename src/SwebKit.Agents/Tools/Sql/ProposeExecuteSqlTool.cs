using System.Text.Json;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Sql;

/// <summary>
/// Proposes executing a mutating SQL statement — returns a pending action for user confirmation.
/// High risk: writes to an external database. The confirmed execution runs through
/// <see cref="SqlActionExecutor"/> which re-checks the connection's AllowWrites toggle and the
/// statement classifier, so the guardrail can't be skipped by a crafted payload.
/// </summary>
public sealed class ProposeExecuteSqlTool : IAgentTool
{
    private readonly IAgentActionCoordinator _coordinator;

    public ProposeExecuteSqlTool(IAgentActionCoordinator coordinator) => _coordinator = coordinator;

    public string Name => "propose_execute_sql";
    public string Description => "Propose executing a mutating SQL statement (INSERT/UPDATE/DELETE/etc.) on a configured connection. Returns a pending action the user must confirm. Only available on connections with writes enabled.";
    public FeatureArea FeatureArea => FeatureArea.Sql;
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.High;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "sql": { "type": "string", "description": "The mutating SQL statement to propose." },
            "connection_id": { "type": "string", "description": "Which configured connection to use. If omitted, uses the active connection." },
            "database": { "type": "string", "description": "Database name. If omitted, uses the connection's configured database." }
          },
          "required": ["sql"]
        }
        """);

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("sql", out var s) || s.GetString() is not { Length: > 0 } sql)
            return Task.FromResult("""{"error":"sql is required."}""");

        var actionId = Guid.NewGuid().ToString("N");
        var summary = $"Execute SQL: {(sql.Length > 80 ? sql[..80] + "…" : sql)}";
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.ExecuteSql,
            Summary = summary,
            Target = "SQL connection",
            Risk = AgentActionRisk.High,
            Preview = sql,
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
            risk = "High",
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "SQL execution proposed. The user must confirm before it runs — and the connection must have writes enabled.",
        }));
    }
}
