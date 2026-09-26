using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// SQL Server/Azure SQL operations for one configured connection. Auth is Entra-only —
/// implementations get a token from <see cref="SwebKit.Core.Services.AzureCredentialFactory"/>.
/// Read-only enforcement is the client's own job: <see cref="ExecuteQueryAsync"/> applies the
/// statement classifier whenever <paramref name="allowWrites"/> is false, so every caller
/// (endpoints, agent tools, the propose→confirm executor) crosses the same boundary.
/// </summary>
public interface ISqlClient : IAsyncDisposable
{
    /// <summary>The configured entry this client was built for.</summary>
    SqlConnectionEntry Connection { get; }

    Task<bool> TestConnectionAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(CancellationToken ct = default);

    /// <summary>The browsable schema tree (schemas → tables/views → columns, indexes, FKs) for
    /// <paramref name="database"/> — or the connection's default database when null.</summary>
    Task<SqlSchemaModel> GetSchemaAsync(string? database, CancellationToken ct = default);

    /// <summary>
    /// The caller's own effective database permissions (<c>sys.fn_my_permissions(NULL, 'DATABASE')</c>).
    /// Any login can query its own row — this is what lets us distinguish "empty database" from
    /// "metadata hidden by policy" in locked-down environments without needing catalog rights.
    /// Returns an empty list when the probe can't run.
    /// </summary>
    Task<IReadOnlyList<string>> GetMyPermissionsAsync(string? database, CancellationToken ct = default);

    /// <summary>
    /// Executes <paramref name="sql"/> and returns up to <paramref name="maxRows"/> rows.
    /// When <paramref name="allowWrites"/> is false the statement batch is classified first and
    /// any mutating statement throws <see cref="SqlWriteGuardException"/> before anything executes.
    /// </summary>
    Task<SqlQueryResult> ExecuteQueryAsync(string sql, string? database, int maxRows, bool allowWrites, CancellationToken ct = default);

    /// <summary>Top-N browse of a table with optional single-column text filter, ordering and
    /// paging. Identifiers are bracket-quoted; the filter is parameterized.</summary>
    Task<SqlQueryResult> GetTableRowsAsync(string schemaName, string tableName, string? database,
        string? filterColumn, string? filterText, string? orderByColumn, bool descending,
        int skip, int take, CancellationToken ct = default);

    /// <summary>Row-level compare of <paramref name="schemaName"/>.<paramref name="tableName"/>
    /// between this connection and <paramref name="target"/>, keyed on
    /// <paramref name="keyColumns"/>. Either side's database may be overridden — a null
    /// database falls back to that connection's configured one.</summary>
    Task<SqlDataCompareResult> CompareDataAsync(ISqlClient target, string schemaName, string tableName,
        IReadOnlyList<string> keyColumns, string? sourceDatabase, string? targetDatabase,
        int maxDiffRows, CancellationToken ct = default);

    /// <summary>Catalog compare between this connection's database and
    /// <paramref name="target"/>'s — report only, no sync scripts. Either side's database may
    /// be overridden independently.</summary>
    Task<SqlSchemaCompareResult> CompareSchemaAsync(ISqlClient target, string? sourceDatabase, string? targetDatabase, CancellationToken ct = default);

    /// <summary>Shallow health check for the agent's check_sql_health tool.</summary>
    Task<SqlHealthReport> CheckHealthAsync(string? database, CancellationToken ct = default);
}

public interface ISqlClientFactory
{
    Task<ISqlClient> CreateAsync(SqlConnectionEntry connection, CancellationToken ct = default);
}

/// <summary>
/// Caches <see cref="ISqlClient"/> instances per connection (keyed by
/// <see cref="SqlConnectionEntry.Id"/>) so repeated requests reuse one client — and its ADO.NET
/// connection pool plus Entra token acquisition — instead of rebuilding per request. Same role
/// as <see cref="IRedisConnectionPool"/>; see docs/pitfalls/azure-sdk.md.
/// </summary>
public interface ISqlConnectionPool
{
    /// <summary>Returns the cached client for the connection entry, creating and caching one if absent.</summary>
    ValueTask<ISqlClient> GetOrCreateAsync(SqlConnectionEntry connection, CancellationToken ct = default);

    /// <summary>Evicts and disposes the cached client for a single connection, if any.</summary>
    void Evict(string connectionId);

    /// <summary>Evicts and disposes every cached client. Safe to call liberally — clients are recreated lazily.</summary>
    void InvalidateAll();
}

/// <summary>
/// Discovers SQL servers (and their databases) across accessible Azure subscriptions via ARM —
/// the SQL analogue of <see cref="IObservabilityResourceDiscovery"/>.
/// </summary>
public interface ISqlResourceDiscovery
{
    IAsyncEnumerable<SqlDiscoveredServer> DiscoverServersAsync(CancellationToken ct = default);

    /// <summary>Clears any in-memory cache so the next call re-scans Azure.</summary>
    void InvalidateCache();
}
