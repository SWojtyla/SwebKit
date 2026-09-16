using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Sql;

/// <summary>
/// Runs a read-only SQL query against a configured connection, hard-capped at
/// <see cref="MaxRows"/> rows — chat context must never get a huge result set. The same
/// ScriptDom write guard the HTTP endpoints use applies here: the agent can never mutate
/// through this tool (writes go through <see cref="ProposeExecuteSqlTool"/> → user confirm).
/// </summary>
public sealed class QuerySqlTool : IAgentTool
{
    private const int MaxRows = 50;

    private readonly AppStateService _appState;
    private readonly ProfileRepository _profiles;
    private readonly ISqlClientFactory _factory;

    public QuerySqlTool(AppStateService appState, ProfileRepository profiles, ISqlClientFactory factory)
    {
        _appState = appState;
        _profiles = profiles;
        _factory = factory;
    }

    public string Name => "query_sql";
    public string Description => $"Runs a read-only SQL query against a configured connection and returns up to {MaxRows} rows. Mutating statements are rejected; ask the user to confirm a write instead.";
    public FeatureArea FeatureArea => FeatureArea.Sql;

    public JsonElement ParametersSchema { get; } = AgentToolSchema.Parse("""
        {
          "type": "object",
          "properties": {
            "sql": { "type": "string", "description": "The SELECT query to run." },
            "connection_id": { "type": "string", "description": "Which configured connection to use. If omitted, uses the active connection." },
            "database": { "type": "string", "description": "Database name. If omitted, uses the connection's configured database." }
          },
          "required": ["sql"]
        }
        """);

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("sql", out var s) || s.GetString() is not { Length: > 0 } sql)
            return """{"error":"sql is required."}""";

        var connectionId = arguments.TryGetProperty("connection_id", out var c) ? c.GetString() : null;
        var database = arguments.TryGetProperty("database", out var d) ? d.GetString() : null;

        var resolution = await SqlToolContext.ResolveAsync(_appState, _profiles, _factory, connectionId, ct);
        if (resolution.Error is not null)
            return JsonSerializer.Serialize(new { error = resolution.Error });

        try
        {
            // Always read-only here regardless of the connection's AllowWrites flag — writes must
            // flow through propose→confirm so a human approves the actual statement.
            var result = await resolution.Client!.ExecuteQueryAsync(sql, database, MaxRows, allowWrites: false, ct);
            return JsonSerializer.Serialize(new
            {
                connection = resolution.Connection!.DisplayName,
                columns = result.Columns.Select(col => col.Name),
                row_count = result.Rows.Count,
                truncated = result.Truncated,
                elapsed_ms = result.ElapsedMs,
                rows = result.Rows,
            });
        }
        catch (SqlWriteGuardException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}
