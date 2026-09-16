using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Sql;

namespace SwebKit.Sidecar.Endpoints;

public static class SqlEndpoints
{
    /// <summary>Default and hard caps for the query endpoint — the grid never gets unbounded rows.</summary>
    private const int DefaultMaxRows = 500;
    private const int HardMaxRows = 5000;
    private const int MaxDiffRows = 500;

    public static void MapSqlEndpoints(this WebApplication app)
    {
        app.MapGet("/api/sql/{connectionId}/test", TestConnectionAsync);
        app.MapGet("/api/sql/{connectionId}/databases", GetDatabasesAsync);
        app.MapGet("/api/sql/{connectionId}/schema", GetSchemaAsync);
        app.MapPost("/api/sql/{connectionId}/query", ExecuteQueryAsync);
        app.MapGet("/api/sql/{connectionId}/tables/{schema}/{table}/rows", GetTableRowsAsync);
        app.MapGet("/api/sql/queries", GetSavedQueriesAsync);
        app.MapPost("/api/sql/queries", SaveQueryAsync);
        app.MapDelete("/api/sql/queries/{queryId}", DeleteQueryAsync);
        app.MapGet("/api/sql/history", GetHistoryAsync);
        app.MapDelete("/api/sql/history", ClearHistoryAsync);
        app.MapGet("/api/sql/discover", DiscoverServersAsync);
        app.MapPost("/api/sql/{connectionId}/completion-context", GetCompletionContext);
        app.MapPost("/api/sql/compare/data", CompareDataAsync);
        app.MapPost("/api/sql/compare/schema", CompareSchemaAsync);
    }

    // ── Test connection ────────────────────────────────────────────────────────

    internal static async Task<IResult> TestConnectionAsync(
        string connectionId,
        ProfileRepository profile,
        ISqlConnectionPool pool,
        DemoModeService demo,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var connection = ResolveConnection(connectionId, profile, demo);
        if (connection is null) return ApiErrors.NotFound("Connection not found");

        try
        {
            var client = await pool.GetOrCreateAsync(connection, ct);
            var ok = await client.TestConnectionAsync(ct);
            return Results.Ok(new { connected = ok });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SQL connection test failed for connection {ConnectionId}", connectionId);
            return Results.Ok(new { connected = false, error = ConnectionTestError.Describe(ex) });
        }
    }

    // ── Server/database enumeration ────────────────────────────────────────────

    internal static async Task<IResult> GetDatabasesAsync(
        string connectionId,
        ProfileRepository profile,
        ISqlConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        var connection = ResolveConnection(connectionId, profile, demo);
        if (connection is null) return ApiErrors.NotFound("Connection not found");

        var client = await pool.GetOrCreateAsync(connection, ct);
        var databases = await client.ListDatabasesAsync(ct);
        return Results.Ok(databases);
    }

    internal static async Task<IResult> GetSchemaAsync(
        string connectionId,
        string? database,
        ProfileRepository profile,
        ISqlConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        var connection = ResolveConnection(connectionId, profile, demo);
        if (connection is null) return ApiErrors.NotFound("Connection not found");

        var client = await pool.GetOrCreateAsync(connection, ct);
        var schema = await client.GetSchemaAsync(database, ct);
        return Results.Ok(schema);
    }

    // ── Query execution ────────────────────────────────────────────────────────

    internal static async Task<IResult> ExecuteQueryAsync(
        string connectionId,
        SqlQueryRequest req,
        ProfileRepository profile,
        ISqlConnectionPool pool,
        DemoModeService demo,
        SqlQueryRepository queries,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var connection = ResolveConnection(connectionId, profile, demo);
        if (connection is null) return ApiErrors.NotFound("Connection not found");
        if (string.IsNullOrWhiteSpace(req.Sql)) return ApiErrors.BadRequest("sql is required");

        var maxRows = Math.Clamp(req.MaxRows ?? DefaultMaxRows, 1, HardMaxRows);
        var client = await pool.GetOrCreateAsync(connection, ct);

        try
        {
            var result = await client.ExecuteQueryAsync(req.Sql, req.Database, maxRows, connection.AllowWrites, ct);
            await RecordHistoryAsync(queries, req, connection.Id, result.ElapsedMs, result.Rows.Count, succeeded: true, error: null, logger);
            return Results.Ok(result);
        }
        catch (SqlWriteGuardException ex)
        {
            await RecordHistoryAsync(queries, req, connection.Id, 0, 0, succeeded: false, ex.Message, logger);
            return ApiErrors.BadRequest(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "SQL query failed on connection {ConnectionId}", connectionId);
            await RecordHistoryAsync(queries, req, connection.Id, 0, 0, succeeded: false, ConnectionTestError.Describe(ex), logger);
            throw;
        }
    }

    // ── Table browse ───────────────────────────────────────────────────────────

    internal static async Task<IResult> GetTableRowsAsync(
        string connectionId,
        string schema,
        string table,
        string? database,
        string? filterColumn,
        string? filter,
        string? orderBy,
        bool? desc,
        int? skip,
        int? take,
        ProfileRepository profile,
        ISqlConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        var connection = ResolveConnection(connectionId, profile, demo);
        if (connection is null) return ApiErrors.NotFound("Connection not found");

        var client = await pool.GetOrCreateAsync(connection, ct);
        var result = await client.GetTableRowsAsync(
            schema, table, database, filterColumn, filter, orderBy, desc ?? false,
            skip ?? 0, Math.Clamp(take ?? 100, 1, 1000), ct);
        return Results.Ok(result);
    }

    // ── Saved queries & history ────────────────────────────────────────────────

    internal static async Task<IResult> GetSavedQueriesAsync(
        string? connectionId,
        SqlQueryRepository queries) =>
        Results.Ok(await queries.GetQueriesAsync(connectionId));

    internal static async Task<IResult> SaveQueryAsync(
        SaveSqlQueryRequest req,
        SqlQueryRepository queries)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return ApiErrors.BadRequest("name is required");
        if (string.IsNullOrWhiteSpace(req.Sql)) return ApiErrors.BadRequest("sql is required");

        var saved = await queries.AddQueryAsync(new SavedSqlQuery
        {
            Name = req.Name.Trim(),
            Folder = req.Folder,
            Sql = req.Sql,
            ConnectionId = req.ConnectionId,
        });
        return Results.Ok(saved);
    }

    internal static async Task<IResult> DeleteQueryAsync(
        string queryId,
        SqlQueryRepository queries) =>
        await queries.DeleteQueryAsync(queryId)
            ? Results.Ok()
            : ApiErrors.NotFound("Query not found");

    internal static async Task<IResult> GetHistoryAsync(
        string? connectionId,
        SqlQueryRepository queries) =>
        Results.Ok(await queries.GetHistoryAsync(connectionId));

    internal static async Task<IResult> ClearHistoryAsync(
        SqlQueryRepository queries)
    {
        await queries.ClearHistoryAsync();
        return Results.Ok();
    }

    // ── ARM discovery ──────────────────────────────────────────────────────────

    internal static async Task<IResult> DiscoverServersAsync(
        ISqlResourceDiscovery discovery,
        CancellationToken ct)
    {
        var servers = new List<SqlDiscoveredServer>();
        await foreach (var server in discovery.DiscoverServersAsync(ct))
            servers.Add(server);
        return Results.Ok(servers);
    }

    // ── Autocomplete context (Phase 2.2) ───────────────────────────────────────

    internal static IResult GetCompletionContext(
        string connectionId,
        SqlCompletionContextRequest req,
        ProfileRepository profile,
        DemoModeService demo)
    {
        var connection = ResolveConnection(connectionId, profile, demo);
        if (connection is null) return ApiErrors.NotFound("Connection not found");

        var context = SqlCompletionResolver.Resolve(req.Sql ?? string.Empty, req.CursorOffset);
        return Results.Ok(context);
    }

    // ── Compare (Phase 2.3/2.4) ────────────────────────────────────────────────

    internal static async Task<IResult> CompareDataAsync(
        SqlDataCompareRequest req,
        ProfileRepository profile,
        ISqlConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        var source = ResolveConnection(req.SourceConnectionId, profile, demo);
        var target = ResolveConnection(req.TargetConnectionId, profile, demo);
        if (source is null || target is null) return ApiErrors.NotFound("Connection not found");
        if (string.IsNullOrWhiteSpace(req.Schema) || string.IsNullOrWhiteSpace(req.Table))
            return ApiErrors.BadRequest("schema and table are required");
        if (req.KeyColumns is not { Count: > 0 })
            return ApiErrors.BadRequest("keyColumns is required");

        var sourceClient = await pool.GetOrCreateAsync(source, ct);
        var targetClient = await pool.GetOrCreateAsync(target, ct);
        var result = await sourceClient.CompareDataAsync(
            targetClient, req.Schema, req.Table, req.KeyColumns, req.Database,
            Math.Clamp(req.MaxDiffRows ?? MaxDiffRows, 1, HardMaxRows), ct);
        return Results.Ok(result);
    }

    internal static async Task<IResult> CompareSchemaAsync(
        SqlSchemaCompareRequest req,
        ProfileRepository profile,
        ISqlConnectionPool pool,
        DemoModeService demo,
        CancellationToken ct)
    {
        var source = ResolveConnection(req.SourceConnectionId, profile, demo);
        var target = ResolveConnection(req.TargetConnectionId, profile, demo);
        if (source is null || target is null) return ApiErrors.NotFound("Connection not found");

        var sourceClient = await pool.GetOrCreateAsync(source, ct);
        var targetClient = await pool.GetOrCreateAsync(target, ct);
        var result = await sourceClient.CompareSchemaAsync(targetClient, req.Database, ct);
        return Results.Ok(result);
    }

    private static async Task RecordHistoryAsync(
        SqlQueryRepository queries, SqlQueryRequest req, string connectionId,
        long elapsedMs, int rowCount, bool succeeded, string? error, ILogger logger)
    {
        try
        {
            await queries.AddHistoryEntryAsync(new SqlHistoryEntry
            {
                Sql = req.Sql!,
                ConnectionId = connectionId,
                Database = req.Database,
                ElapsedMs = elapsedMs,
                RowCount = rowCount,
                Succeeded = succeeded,
                Error = error,
            });
        }
        catch (Exception ex)
        {
            // History is best-effort — a failed write must never fail the query itself.
            logger.LogWarning(ex, "Failed to record SQL history entry");
        }
    }

    private static SqlConnectionEntry? ResolveConnection(
        string connectionId,
        ProfileRepository profile,
        DemoModeService demo)
    {
        if (demo.IsDemoMode)
            return demo.GetDemoSqlConnection(connectionId);

        // "demo-sql"/"demo-sql-2" are reserved ids — a save made while demo mode was on can
        // persist the overlay into the profile, and that copy must never resolve to a real client.
        if (connectionId is DemoModeService.DemoSqlConnectionId or DemoModeService.DemoSqlConnectionId2)
            return null;

        var config = profile.GetProfileData().Config.SqlConfig;
        return config?.Connections.FirstOrDefault(c => c.Id == connectionId);
    }

    public sealed record SqlQueryRequest(string? Sql, string? Database, int? MaxRows);
    public sealed record SaveSqlQueryRequest(string? Name, string? Folder, string? Sql, string? ConnectionId);
    public sealed record SqlCompletionContextRequest(string? Sql, int CursorOffset);
    public sealed record SqlDataCompareRequest(
        string SourceConnectionId, string TargetConnectionId,
        string? Schema, string? Table, List<string>? KeyColumns,
        string? Database, int? MaxDiffRows);
    public sealed record SqlSchemaCompareRequest(
        string SourceConnectionId, string TargetConnectionId, string? Database);
}
