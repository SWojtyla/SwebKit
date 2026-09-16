using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Sql;

/// <summary>Describes one table/view: columns with types, nullability, keys, indexes, FKs.</summary>
public sealed class DescribeSqlTableTool : IAgentTool
{
    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly ISqlClientFactory _factory;

    public DescribeSqlTableTool(AppStateService appState, ProfileRepository profiles, ISqlClientFactory factory)
    {
        _appState = appState;
        _profiles = profiles;
        _factory = factory;
    }

    public string Name => "describe_sql_table";
    public string Description => "Describes a table or view: columns with types and nullability, primary key, indexes, foreign keys.";
    public FeatureArea FeatureArea => FeatureArea.Sql;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "table": { "type": "string", "description": "Table or view name." },
            "schema": { "type": "string", "description": "Schema name. Defaults to 'dbo'." },
            "connection_id": { "type": "string", "description": "Which configured connection to use. If omitted, uses the active connection." },
            "database": { "type": "string", "description": "Database name. If omitted, uses the connection's configured database." }
          },
          "required": ["table"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("table", out var t) || t.GetString() is not { Length: > 0 } table)
            return """{"error":"table is required."}""";

        var schema = arguments.TryGetProperty("schema", out var s) && s.GetString() is { Length: > 0 } sv ? sv : "dbo";
        var connectionId = arguments.TryGetProperty("connection_id", out var c) ? c.GetString() : null;
        var database = arguments.TryGetProperty("database", out var d) ? d.GetString() : null;

        var resolution = await SqlToolContext.ResolveAsync(_appState, _profiles, _factory, connectionId, ct);
        if (resolution.Error is not null)
            return JsonSerializer.Serialize(new { error = resolution.Error });

        try
        {
            var model = await resolution.Client!.GetSchemaAsync(database, ct);
            var tableModel = model.Schemas
                .FirstOrDefault(s => s.Name.Equals(schema, StringComparison.OrdinalIgnoreCase))
                ?.Objects.FirstOrDefault(x => x.Name.Equals(table, StringComparison.OrdinalIgnoreCase));

            if (tableModel is null)
                return JsonSerializer.Serialize(new { error = $"Table '{schema}.{table}' not found." });

            return JsonSerializer.Serialize(new
            {
                connection = resolution.Connection!.DisplayName,
                schema,
                table = tableModel.Name,
                type = tableModel.Kind,
                columns = tableModel.Columns.Select(col => new
                {
                    name = col.Name,
                    data_type = col.DataType,
                    nullable = col.IsNullable,
                    is_primary_key = col.IsPrimaryKey,
                }),
                indexes = tableModel.Indexes.Select(i => new { name = i.Name, unique = i.IsUnique, columns = i.Columns }),
                foreign_keys = tableModel.ForeignKeys.Select(f => new
                {
                    name = f.Name,
                    references = f.ReferencedObject,
                }),
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}
