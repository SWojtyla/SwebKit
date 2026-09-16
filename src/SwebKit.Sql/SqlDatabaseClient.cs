using System.Diagnostics;
using Azure.Core;
using Microsoft.Data.SqlClient;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Sql;

/// <summary>
/// <see cref="ISqlClient"/> on <see cref="SqlConnection"/>. The client itself is stateless —
/// ADO.NET's built-in connection pool (keyed on the connection string) does the real pooling,
/// so each operation opens a fresh logical connection cheaply. The sidecar's
/// <c>SidecarSqlConnectionPool</c> caches this client per connection entry to avoid rebuilding
/// the credential per request.
/// </summary>
/// <remarks>
/// Entra auth uses <see cref="SqlConnection.AccessTokenCallback"/> wired to the shared
/// <see cref="AzureCredentialFactory"/> rather than <c>Authentication=Active Directory
/// Default</c> — the latter runs MSAL's own credential chain and would reintroduce the
/// EnvironmentCredential pitfall (docs/pitfalls/azure-sdk.md AZ-4). The callback (not a static
/// AccessToken) means pooled connections get a fresh token after expiry.
/// </remarks>
public sealed class SqlDatabaseClient : ISqlClient
{
    /// <summary>Token scope for Azure SQL / SQL Server Entra auth.</summary>
    private static readonly string[] SqlTokenScope = ["https://database.windows.net/.default"];

    /// <summary>Row cap for the unbounded fetch inside data compare — compare of larger tables
    /// returns a partial result marked truncated rather than paging forever.</summary>
    internal const int CompareFetchCap = 100_000;

    private readonly TokenCredential _credential;

    public SqlDatabaseClient(SqlConnectionEntry connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrWhiteSpace(connection.Server))
            throw new InvalidOperationException($"{nameof(SqlConnectionEntry.Server)} is required for connection '{connection.DisplayName}'.");
        Connection = connection;
        _credential = AzureCredentialFactory.CreateDefault();
    }

    public SqlConnectionEntry Connection { get; }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection("master");
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT 1", conn);
        await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection("master");
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            "SELECT name, state_desc FROM sys.databases ORDER BY name", conn);
        var databases = new List<SqlDatabaseInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            databases.Add(new SqlDatabaseInfo(reader.GetString(0), reader.GetString(1)));
        return databases;
    }

    public async Task<SqlSchemaModel> GetSchemaAsync(string? database, CancellationToken ct = default)
    {
        await using var conn = CreateConnection(database);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var model = new SqlSchemaModel { Database = EffectiveDatabase(database) };
        var schemas = new Dictionary<string, SqlSchemaGroup>(StringComparer.OrdinalIgnoreCase);
        var objects = new Dictionary<string, SqlObjectInfo>(StringComparer.OrdinalIgnoreCase);

        await using (var cmd = new SqlCommand("""
            SELECT s.name AS schema_name,
                   o.name AS object_name,
                   o.type AS object_type,
                   c.name AS column_name,
                   t.name AS data_type,
                   c.is_nullable,
                   CASE WHEN pk.column_id IS NULL THEN CONVERT(bit, 0) ELSE CONVERT(bit, 1) END AS is_pk
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            LEFT JOIN sys.columns c ON c.object_id = o.object_id
            LEFT JOIN sys.types t ON t.user_type_id = c.user_type_id
            LEFT JOIN (
                SELECT ic.object_id, ic.column_id
                FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                WHERE i.is_primary_key = 1
            ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
            WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0
            ORDER BY s.name, o.name, c.column_id
            """, conn))
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var schemaName = reader.GetString(0);
                var objectName = reader.GetString(1);
                var key = $"{schemaName}.{objectName}";

                if (!schemas.TryGetValue(schemaName, out var schema))
                {
                    schema = new SqlSchemaGroup { Name = schemaName };
                    schemas[schemaName] = schema;
                    model.Schemas.Add(schema);
                }
                if (!objects.TryGetValue(key, out var obj))
                {
                    obj = new SqlObjectInfo
                    {
                        Name = objectName,
                        Kind = reader.GetString(2).Trim() == "V" ? "view" : "table",
                    };
                    objects[key] = obj;
                    schema.Objects.Add(obj);
                }
                if (!reader.IsDBNull(3))
                {
                    obj.Columns.Add(new SqlColumnInfo
                    {
                        Name = reader.GetString(3),
                        DataType = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        IsNullable = !reader.IsDBNull(5) && reader.GetBoolean(5),
                        IsPrimaryKey = !reader.IsDBNull(6) && reader.GetBoolean(6),
                    });
                }
            }
        }

        // Indexes and foreign keys — a second pass so each object's list is populated for
        // schema compare and the tree's detail view.
        await using (var cmd = new SqlCommand("""
            SELECT s.name AS schema_name, o.name AS object_name,
                   i.name AS index_name, i.is_unique, i.is_primary_key,
                   STUFF((SELECT ',' + c2.name
                          FROM sys.index_columns ic2
                          JOIN sys.columns c2 ON c2.object_id = ic2.object_id AND c2.column_id = ic2.column_id
                          WHERE ic2.object_id = i.object_id AND ic2.index_id = i.index_id
                          ORDER BY ic2.key_ordinal
                          FOR XML PATH('')), 1, 1, '') AS column_list
            FROM sys.indexes i
            JOIN sys.objects o ON o.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0 AND i.name IS NOT NULL
            ORDER BY s.name, o.name, i.name
            """, conn))
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var key = $"{reader.GetString(0)}.{reader.GetString(1)}";
                if (!objects.TryGetValue(key, out var obj))
                    continue;
                var columnList = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
                obj.Indexes.Add(new SqlIndexInfo
                {
                    Name = reader.GetString(2),
                    IsUnique = reader.GetBoolean(3),
                    IsPrimaryKey = reader.GetBoolean(4),
                    Columns = columnList.Length == 0 ? [] : columnList.Split(',').ToList(),
                });
            }
        }

        await using (var cmd = new SqlCommand("""
            SELECT s.name AS schema_name, o.name AS object_name,
                   fk.name AS fk_name,
                   rs.name + '.' + ro.name AS referenced_object
            FROM sys.foreign_keys fk
            JOIN sys.objects o ON o.object_id = fk.parent_object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            JOIN sys.objects ro ON ro.object_id = fk.referenced_object_id
            JOIN sys.schemas rs ON rs.schema_id = ro.schema_id
            WHERE o.is_ms_shipped = 0
            ORDER BY s.name, o.name, fk.name
            """, conn))
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var key = $"{reader.GetString(0)}.{reader.GetString(1)}";
                if (objects.TryGetValue(key, out var obj))
                {
                    obj.ForeignKeys.Add(new SqlForeignKeyInfo
                    {
                        Name = reader.GetString(2),
                        ReferencedObject = reader.GetString(3),
                    });
                }
            }
        }

        return model;
    }

    public async Task<SqlQueryResult> ExecuteQueryAsync(string sql, string? database, int maxRows, bool allowWrites, CancellationToken ct = default)
    {
        if (!allowWrites)
            SqlStatementGuard.ThrowIfNotReadOnly(sql);

        var started = Stopwatch.GetTimestamp();
        await using var conn = CreateConnection(database);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };

        var result = new SqlQueryResult();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        if (reader.FieldCount > 0)
        {
            var names = DeduplicateNames(reader);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var typeName = "(unknown)";
                try { typeName = reader.GetDataTypeName(i); } catch { /* some providers lack it */ }
                result.Columns.Add(new SqlResultColumn { Name = names[i], TypeName = typeName });
            }

            while (result.Rows.Count <= maxRows && await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var i = 0; i < reader.FieldCount; i++)
                    row[names[i]] = NormalizeValue(reader.GetValue(i));
                result.Rows.Add(row);
            }
            if (result.Rows.Count > maxRows)
            {
                result.Rows.RemoveAt(result.Rows.Count - 1);
                result.Truncated = true;
            }
        }
        result.RowsAffected = reader.RecordsAffected;
        result.ElapsedMs = ElapsedMs(started);
        return result;
    }

    public async Task<SqlQueryResult> GetTableRowsAsync(string schemaName, string tableName, string? database,
        string? filterColumn, string? filterText, string? orderByColumn, bool descending,
        int skip, int take, CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        var sql = $"SELECT * FROM {Quote(schemaName)}.{Quote(tableName)}";
        await using var conn = CreateConnection(database);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand { Connection = conn, CommandTimeout = 60 };

        if (!string.IsNullOrWhiteSpace(filterColumn) && !string.IsNullOrEmpty(filterText))
        {
            sql += $" WHERE CAST({Quote(filterColumn)} AS nvarchar(max)) LIKE @filter";
            cmd.Parameters.AddWithValue("@filter", $"%{filterText}%");
        }

        // OFFSET/FETCH requires ORDER BY — (SELECT 0) is the conventional no-op ordering.
        sql += !string.IsNullOrWhiteSpace(orderByColumn)
            ? $" ORDER BY {Quote(orderByColumn)} {(descending ? "DESC" : "ASC")}"
            : " ORDER BY (SELECT 0)";
        sql += " OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        cmd.Parameters.AddWithValue("@skip", Math.Max(0, skip));
        cmd.Parameters.AddWithValue("@take", take + 1); // one extra row = truncation marker
        cmd.CommandText = sql;

        var result = new SqlQueryResult();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var names = DeduplicateNames(reader);
        for (var i = 0; i < reader.FieldCount; i++)
            result.Columns.Add(new SqlResultColumn { Name = names[i], TypeName = reader.GetDataTypeName(i) });

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
                row[names[i]] = NormalizeValue(reader.GetValue(i));
            result.Rows.Add(row);
        }
        if (result.Rows.Count > take)
        {
            result.Rows.RemoveAt(result.Rows.Count - 1);
            result.Truncated = true;
        }
        result.ElapsedMs = ElapsedMs(started);
        return result;
    }

    public async Task<SqlDataCompareResult> CompareDataAsync(ISqlClient target, string schemaName, string tableName,
        IReadOnlyList<string> keyColumns, string? database, int maxDiffRows, CancellationToken ct = default)
    {
        var sourceRows = await FetchAllRowsAsync(database, schemaName, tableName, ct).ConfigureAwait(false);
        var targetRows = await FetchAllRowsAsync(target, database, schemaName, tableName, ct).ConfigureAwait(false);

        var result = SqlDataComparer.Compare(sourceRows.Rows, targetRows.Rows, keyColumns, maxDiffRows);
        if (sourceRows.Truncated || targetRows.Truncated)
        {
            result.Truncated = true;
            result.SchemaWarnings.Add(
                $"Row fetch capped at {CompareFetchCap} rows per side — compare result is partial.");
        }
        return result;
    }

    public async Task<SqlSchemaCompareResult> CompareSchemaAsync(ISqlClient target, string? database, CancellationToken ct = default)
    {
        var source = await GetSchemaAsync(database, ct).ConfigureAwait(false);
        var targetModel = await target.GetSchemaAsync(database, ct).ConfigureAwait(false);
        return SqlSchemaComparer.Compare(source, targetModel);
    }

    public async Task<SqlHealthReport> CheckHealthAsync(string? database, CancellationToken ct = default)
    {
        var report = new SqlHealthReport();
        try
        {
            await using var conn = CreateConnection(database);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            report.Connected = true;

            await using (var cmd = new SqlCommand("SELECT @@SERVERNAME, @@VERSION", conn))
            await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    report.ServerName = reader.GetString(0);
                    // @@VERSION is a multi-line banner — the first line carries the product name.
                    report.Version = reader.GetString(1).Split('\n')[0].Trim();
                }
            }

            var db = EffectiveDatabase(database);
            await using (var cmd = new SqlCommand("SELECT state_desc FROM sys.databases WHERE name = @db", conn))
            {
                cmd.Parameters.AddWithValue("@db", db);
                var state = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                report.DatabaseState = state as string;
            }

            await using (var cmd = new SqlCommand(
                "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id <> 0", conn))
            {
                report.BlockingSessionCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
            }

            if (report.BlockingSessionCount > 0)
                report.Notes.Add($"{report.BlockingSessionCount} session(s) currently waiting on locks.");
            if (report.DatabaseState is not null and not "ONLINE")
                report.Notes.Add($"Database '{db}' state is {report.DatabaseState}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report.Connected = false;
            report.Notes.Add(ex.Message);
        }
        return report;
    }

    public ValueTask DisposeAsync()
    {
        // Stateless — every operation opened its own logical connection through the ADO.NET pool.
        return ValueTask.CompletedTask;
    }

    // ── Internals ───────────────────────────────────────────────────────────

    private SqlConnection CreateConnection(string? database)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = NormalizeServer(Connection.Server),
            InitialCatalog = EffectiveDatabase(database),
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = false,
            PersistSecurityInfo = false,
            ConnectTimeout = 15,
        };
        return new SqlConnection(builder.ConnectionString)
        {
            AccessTokenCallback = async (_, cancellationToken) =>
            {
                var token = await _credential.GetTokenAsync(
                    new TokenRequestContext(SqlTokenScope), cancellationToken).ConfigureAwait(false);
                return new SqlAuthenticationToken(token.Token, token.ExpiresOn);
            },
        };
    }

    private string EffectiveDatabase(string? database) =>
        !string.IsNullOrWhiteSpace(database) ? database
        : !string.IsNullOrWhiteSpace(Connection.Database) ? Connection.Database
        : "master";

    /// <summary>Accepts "server" / "server,port" / "tcp:server" / "host\instance" and produces a
    /// DataSource value. Port 1433 is appended only for bare hostnames (not named instances).</summary>
    internal static string NormalizeServer(string server)
    {
        var s = server.Trim();
        if (s.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            s = s[4..];
        if (s.Contains('\\') || s.Contains(','))
            return $"tcp:{s}";
        return $"tcp:{s},1433";
    }

    /// <summary>Bracket-quotes a SQL identifier — the only safe way to interpolate
    /// schema/table/column names that came from the browser.</summary>
    internal static string Quote(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private async Task<(List<Dictionary<string, object?>> Rows, bool Truncated)> FetchAllRowsAsync(
        string? database, string schemaName, string tableName, CancellationToken ct)
    {
        var result = await ExecuteQueryAsync(
            $"SELECT * FROM {Quote(schemaName)}.{Quote(tableName)}", database,
            CompareFetchCap + 1, allowWrites: false, ct).ConfigureAwait(false);
        var truncated = result.Rows.Count > CompareFetchCap;
        return (result.Rows.Take(CompareFetchCap).ToList(), truncated);
    }

    private static async Task<(List<Dictionary<string, object?>> Rows, bool Truncated)> FetchAllRowsAsync(
        ISqlClient client, string? database, string schemaName, string tableName, CancellationToken ct)
    {
        var result = await client.ExecuteQueryAsync(
            $"SELECT * FROM {Quote(schemaName)}.{Quote(tableName)}", database,
            CompareFetchCap + 1, allowWrites: false, ct).ConfigureAwait(false);
        var truncated = result.Rows.Count > CompareFetchCap;
        return (result.Rows.Take(CompareFetchCap).ToList(), truncated);
    }

    /// <summary>Distinct display names for result columns — unnamed expressions get
    /// "column{n}", duplicates get a numeric suffix so the row dictionary can't drop a field.</summary>
    private static List<string> DeduplicateNames(SqlDataReader reader)
    {
        var names = new List<string>(reader.FieldCount);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var raw = reader.GetName(i);
            var name = string.IsNullOrEmpty(raw) ? $"column{i + 1}" : raw;
            if (seen.TryGetValue(name, out var count))
            {
                seen[name] = count + 1;
                name = $"{name}_{count + 1}";
            }
            else
            {
                seen[name] = 1;
            }
            names.Add(name);
        }
        return names;
    }

    private static object? NormalizeValue(object? value) =>
        value is DBNull ? null : value;

    private static long ElapsedMs(long started) =>
        (long)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
}
