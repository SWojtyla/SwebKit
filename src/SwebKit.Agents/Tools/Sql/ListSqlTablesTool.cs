using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Sql;

/// <summary>Lists tables/views (with optional schema filter) in a database.</summary>
public sealed class ListSqlTablesTool : IAgentTool
{
    private const int MaxObjects = 100;

    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly ISqlClientFactory _factory;

    public ListSqlTablesTool(AppStateService appState, ProfileRepository profiles, ISqlClientFactory factory)
    {
        _appState = appState;
        _profiles = profiles;
        _factory = factory;
    }

    public string Name => "list_sql_tables";
    public string Description => $"Lists up to {MaxObjects} tables and views in a database, optionally filtered by schema.";
    public FeatureArea FeatureArea => FeatureArea.Sql;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "connection_id": { "type": "string", "description": "Which configured connection to use. If omitted, uses the active connection." },
            "database": { "type": "string", "description": "Database name. If omitted, uses the connection's configured database." },
            "schema": { "type": "string", "description": "Optional schema filter (e.g. 'dbo')." }
          },
          "required": []
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var connectionId = arguments.TryGetProperty("connection_id", out var c) ? c.GetString() : null;
        var database = arguments.TryGetProperty("database", out var d) ? d.GetString() : null;
        var schemaFilter = arguments.TryGetProperty("schema", out var s) ? s.GetString() : null;

        var resolution = await SqlToolContext.ResolveAsync(_appState, _profiles, _factory, connectionId, ct);
        if (resolution.Error is not null)
            return JsonSerializer.Serialize(new { error = resolution.Error });

        try
        {
            var model = await resolution.Client!.GetSchemaAsync(database, ct);
            var schemas = model.Schemas
                .Where(s => schemaFilter is null || s.Name.Equals(schemaFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var objects = schemas
                .SelectMany(s => s.Objects.Select(t => new { schema = s.Name, name = t.Name, type = t.Kind, columns = t.Columns.Count }))
                .Take(MaxObjects)
                .ToList();
            return JsonSerializer.Serialize(new
            {
                connection = resolution.Connection!.DisplayName,
                database = model.Database,
                object_count = objects.Count,
                more_available = schemas.Sum(s => s.Objects.Count) > objects.Count,
                objects,
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}
