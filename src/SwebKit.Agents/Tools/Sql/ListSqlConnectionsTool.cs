using System.Text.Json;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Sql;

/// <summary>Lists the configured SQL connections so the agent (and user) can pick one by id.</summary>
public sealed class ListSqlConnectionsTool : IAgentTool
{
    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;

    public ListSqlConnectionsTool(AppStateService appState, ProfileRepository profiles)
    {
        _appState = appState;
        _profiles = profiles;
    }

    public string Name => "list_sql_connections";
    public string Description => "Lists the configured SQL Server/Azure SQL connections (ids, servers, databases, read-only vs write-enabled).";
    public FeatureArea FeatureArea => FeatureArea.Sql;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {},
          "required": []
        }
        """);

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (_appState.UseDemoData)
        {
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                demo_mode = true,
                connections = new[]
                {
                    new { id = SqlToolContext.DemoConnectionId, display_name = "orders-dev-sql", server = "orders-dev-sql.database.windows.net", database = "orders", read_only = true },
                },
            }));
        }

        var config = _profiles.GetProfileData().Config.SqlConfig;
        var connections = config?.Connections ?? [];
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            demo_mode = false,
            active_connection_id = config?.ActiveConnectionId,
            connections = connections.Select(c => new
            {
                id = c.Id,
                display_name = c.DisplayName,
                server = c.Server,
                database = c.Database,
                read_only = !c.AllowWrites,
            }),
        }));
    }
}
