using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Sql;

/// <summary>Lists databases on the configured SQL connection.</summary>
public sealed class ListSqlDatabasesTool : IAgentTool
{
    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly ISqlClientFactory _factory;

    public ListSqlDatabasesTool(AppStateService appState, ProfileRepository profiles, ISqlClientFactory factory)
    {
        _appState = appState;
        _profiles = profiles;
        _factory = factory;
    }

    public string Name => "list_sql_databases";
    public string Description => "Lists the databases visible on a configured SQL connection.";
    public FeatureArea FeatureArea => FeatureArea.Sql;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "connection_id": { "type": "string", "description": "Which configured connection to use. If omitted, uses the active connection." }
          },
          "required": []
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var connectionId = arguments.TryGetProperty("connection_id", out var c) ? c.GetString() : null;
        var resolution = await SqlToolContext.ResolveAsync(_appState, _profiles, _factory, connectionId, ct);
        if (resolution.Error is not null)
            return JsonSerializer.Serialize(new { error = resolution.Error });

        try
        {
            var databases = await resolution.Client!.ListDatabasesAsync(ct);
            return JsonSerializer.Serialize(new
            {
                connection = resolution.Connection!.DisplayName,
                databases = databases.Select(d => new { name = d.Name, status = d.State }),
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}
