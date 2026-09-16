using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Sql;

/// <summary>Shallow health check — connectivity, database state, blocking-session count.
/// Deliberately shallow per the plan: this powers investigate_workspace_issue, not a DBA dashboard.</summary>
public sealed class CheckSqlHealthTool : IAgentTool
{
    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly ISqlClientFactory _factory;

    public CheckSqlHealthTool(AppStateService appState, ProfileRepository profiles, ISqlClientFactory factory)
    {
        _appState = appState;
        _profiles = profiles;
        _factory = factory;
    }

    public string Name => "check_sql_health";
    public string Description => "Runs a shallow health check on a configured SQL connection: connectivity, server version, database state, blocking-session count.";
    public FeatureArea FeatureArea => FeatureArea.Sql;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "connection_id": { "type": "string", "description": "Which configured connection to use. If omitted, uses the active connection." },
            "database": { "type": "string", "description": "Database name. If omitted, uses the connection's configured database." }
          },
          "required": []
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var connectionId = arguments.TryGetProperty("connection_id", out var c) ? c.GetString() : null;
        var database = arguments.TryGetProperty("database", out var d) ? d.GetString() : null;

        var resolution = await SqlToolContext.ResolveAsync(_appState, _profiles, _factory, connectionId, ct);
        if (resolution.Error is not null)
            return JsonSerializer.Serialize(new { error = resolution.Error });

        try
        {
            var report = await resolution.Client!.CheckHealthAsync(database, ct);
            return JsonSerializer.Serialize(new
            {
                connection = resolution.Connection!.DisplayName,
                connected = report.Connected,
                server_name = report.ServerName,
                version = report.Version,
                database_state = report.DatabaseState,
                blocking_sessions = report.BlockingSessionCount,
                notes = report.Notes,
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, connected = false });
        }
    }
}
