using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// Synthetic <see cref="ISqlClient"/> for demo mode — a small "orders" database
/// (dbo.customers / dbo.orders / dbo.products / sales.invoices) with deterministic rows.
/// Variant 1 (the second demo connection) carries a slightly different catalog and data so the
/// schema/data compare panels show a real diff in demo mode.
/// Variant 2 (the "prd" connection) simulates a locked-down environment: the schema catalog
/// reads empty and <see cref="GetMyPermissionsAsync"/> reports SELECT/EXECUTE without
/// VIEW DEFINITION — while queries still work, matching a real "metadata hidden" grant.
/// </summary>
public sealed class DemoSqlClient : ISqlClient
{
    private readonly int _variant;

    public DemoSqlClient(SqlConnectionEntry connection, int variant = 0)
    {
        Connection = connection;
        _variant = variant;
    }

    public SqlConnectionEntry Connection { get; }

    /// <summary>Variant 2 models a PRD login: data-plane rights, no catalog visibility.</summary>
    private bool MetadataHidden => _variant == 2;

    public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task<IReadOnlyList<string>> GetMyPermissionsAsync(string? database, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(MetadataHidden
            ? ["EXECUTE", "SELECT"]
            : ["CONTROL", "DELETE", "EXECUTE", "INSERT", "SELECT", "UPDATE", "VIEW DEFINITION"]);

    public Task<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SqlDatabaseInfo>>(
        [
            new SqlDatabaseInfo("master", "ONLINE"),
            new SqlDatabaseInfo("orders", "ONLINE"),
        ]);

    public Task<SqlSchemaModel> GetSchemaAsync(string? database, CancellationToken ct = default)
    {
        var model = new SqlSchemaModel { Database = database ?? "orders" };
        if (MetadataHidden)
            return Task.FromResult(model); // what sys.objects returns with no VIEW DEFINITION
        var dbo = new SqlSchemaGroup { Name = "dbo" };
        var sales = new SqlSchemaGroup { Name = "sales" };

        dbo.Objects.Add(new SqlObjectInfo
        {
            Name = "customers",
            Kind = "table",
            Columns =
            [
                Col("id", "int", pk: true),
                Col("name", "nvarchar"),
                Col("email", "nvarchar", nullable: true),
                Col("city", "nvarchar"),
            ],
            Indexes = [new SqlIndexInfo { Name = "PK_customers", IsUnique = true, IsPrimaryKey = true, Columns = ["id"] }],
        });
        dbo.Objects.Add(new SqlObjectInfo
        {
            Name = "orders",
            Kind = "table",
            Columns =
            [
                Col("id", "int", pk: true),
                Col("customer_id", "int"),
                Col("order_date", "datetime2"),
                Col("total", "decimal"),
                Col("status", "nvarchar"),
            ],
            Indexes = [new SqlIndexInfo { Name = "PK_orders", IsUnique = true, IsPrimaryKey = true, Columns = ["id"] }],
            ForeignKeys = [new SqlForeignKeyInfo { Name = "FK_orders_customers", ReferencedObject = "dbo.customers" }],
        });
        dbo.Objects.Add(new SqlObjectInfo
        {
            Name = "products",
            Kind = "table",
            Columns =
            [
                Col("id", "int", pk: true),
                Col("name", "nvarchar"),
                Col("price", "decimal"),
                Col("stock", "int"),
            ],
            Indexes = [new SqlIndexInfo { Name = "PK_products", IsUnique = true, IsPrimaryKey = true, Columns = ["id"] }],
        });
        sales.Objects.Add(new SqlObjectInfo
        {
            Name = "invoices",
            Kind = "table",
            Columns =
            [
                Col("id", "int", pk: true),
                Col("order_id", "int"),
                Col("amount", "decimal"),
                Col("issued_at", "datetime2", nullable: true),
            ],
            Indexes = [new SqlIndexInfo { Name = "PK_invoices", IsUnique = true, IsPrimaryKey = true, Columns = ["id"] }],
            ForeignKeys = [new SqlForeignKeyInfo { Name = "FK_invoices_orders", ReferencedObject = "dbo.orders" }],
        });

        if (_variant == 1)
        {
            // The second demo connection drifts from the first so compare panels show a diff:
            // an extra table, one missing column on products, a changed row.
            sales.Objects.Add(new SqlObjectInfo
            {
                Name = "returns",
                Kind = "table",
                Columns =
                [
                    Col("id", "int", pk: true),
                    Col("order_id", "int"),
                    Col("reason", "nvarchar", nullable: true),
                ],
            });
            var products = dbo.Objects.First(o => o.Name == "products");
            products.Columns.RemoveAll(c => c.Name == "stock");
        }

        model.Schemas.Add(dbo);
        model.Schemas.Add(sales);
        return Task.FromResult(model);
    }

    public Task<SqlQueryResult> ExecuteQueryAsync(string sql, string? database, int maxRows, bool allowWrites, CancellationToken ct = default)
    {
        if (!allowWrites && !LooksReadOnly(sql))
            throw new SqlWriteGuardException("Only read-only statements are allowed on this connection.");

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var normalized = sql.TrimStart().ToLowerInvariant();

        var result = new SqlQueryResult();
        if (normalized.Contains("from customers") || normalized.Contains("from dbo.customers"))
            result = RowsToResult(CustomerRows(), maxRows);
        else if (normalized.Contains("from orders") || normalized.Contains("from dbo.orders"))
            result = RowsToResult(OrderRows(), maxRows);
        else if (normalized.Contains("from products") || normalized.Contains("from dbo.products"))
            result = RowsToResult(ProductRows(), maxRows);
        else if (normalized.Contains("from invoices") || normalized.Contains("from sales.invoices"))
            result = RowsToResult(InvoiceRows(), maxRows);
        else
            result = new SqlQueryResult
            {
                Columns = [new SqlResultColumn { Name = "value", TypeName = "int" }],
                Rows = [new Dictionary<string, object?> { ["value"] = 1 }],
            };

        result.ElapsedMs = Elapsed(started);
        return Task.FromResult(result);
    }

    public Task<SqlQueryResult> GetTableRowsAsync(string schemaName, string tableName, string? database,
        string? filterColumn, string? filterText, string? orderByColumn, bool descending,
        int skip, int take, CancellationToken ct = default)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var rows = RowsFor(schemaName, tableName);

        if (!string.IsNullOrEmpty(filterColumn) && !string.IsNullOrEmpty(filterText))
        {
            rows = rows.Where(r =>
                r.TryGetValue(filterColumn, out var v) &&
                v?.ToString()?.Contains(filterText, StringComparison.OrdinalIgnoreCase) == true).ToList();
        }

        if (!string.IsNullOrEmpty(orderByColumn))
        {
            rows = descending
                ? rows.OrderByDescending(r => r.GetValueOrDefault(orderByColumn)?.ToString(), StringComparer.Ordinal).ToList()
                : rows.OrderBy(r => r.GetValueOrDefault(orderByColumn)?.ToString(), StringComparer.Ordinal).ToList();
        }

        var page = rows.Skip(skip).Take(take + 1).ToList();
        var truncated = page.Count > take;
        var result = new SqlQueryResult
        {
            Columns = page.FirstOrDefault()?.Keys.Select(k => new SqlResultColumn { Name = k, TypeName = "nvarchar" }).ToList()
                ?? rows.FirstOrDefault()?.Keys.Select(k => new SqlResultColumn { Name = k, TypeName = "nvarchar" }).ToList()
                ?? [],
            Rows = page.Take(take).ToList(),
            Truncated = truncated,
            ElapsedMs = Elapsed(started),
        };
        return Task.FromResult(result);
    }

    public async Task<SqlDataCompareResult> CompareDataAsync(ISqlClient target, string schemaName, string tableName,
        IReadOnlyList<string> keyColumns, string? sourceDatabase, string? targetDatabase,
        int maxDiffRows, CancellationToken ct = default)
    {
        var sourceRows = RowsFor(schemaName, tableName);
        var targetRows = target is DemoSqlClient demo
            ? demo.RowsFor(schemaName, tableName)
            : (await target.ExecuteQueryAsync($"SELECT * FROM [{schemaName}].[{tableName}]", targetDatabase, maxDiffRows + 1, allowWrites: false, ct)).Rows;
        return SqlDataComparer.Compare(sourceRows, targetRows, keyColumns, maxDiffRows);
    }

    public async Task<SqlSchemaCompareResult> CompareSchemaAsync(ISqlClient target, string? sourceDatabase, string? targetDatabase, CancellationToken ct = default)
    {
        var source = await GetSchemaAsync(sourceDatabase, ct);
        var targetModel = await target.GetSchemaAsync(targetDatabase, ct);
        return SqlSchemaComparer.Compare(source, targetModel);
    }

    public Task<SqlHealthReport> CheckHealthAsync(string? database, CancellationToken ct = default) =>
        Task.FromResult(new SqlHealthReport
        {
            Connected = true,
            ServerName = "demo-sql",
            Version = "Microsoft SQL Server (demo)",
            DatabaseState = "ONLINE",
            BlockingSessionCount = _variant == 1 ? 2 : 0,
            Notes = _variant == 1 ? ["2 sessions waiting on locks (demo data)."] : [],
        });

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Canned data ─────────────────────────────────────────────────────────

    private List<Dictionary<string, object?>> RowsFor(string schemaName, string tableName) =>
        (schemaName, tableName) switch
        {
            ("dbo", "customers") => CustomerRows(),
            ("dbo", "orders") => OrderRows(),
            ("dbo", "products") => ProductRows(),
            ("sales", "invoices") => InvoiceRows(),
            ("sales", "returns") when _variant == 1 => ReturnRows(),
            _ => [],
        };

    private static SqlColumnInfo Col(string name, string type, bool nullable = false, bool pk = false) =>
        new() { Name = name, DataType = type, IsNullable = nullable, IsPrimaryKey = pk };

    private static SqlQueryResult RowsToResult(List<Dictionary<string, object?>> rows, int maxRows)
    {
        var truncated = rows.Count > maxRows;
        return new SqlQueryResult
        {
            Columns = rows.FirstOrDefault()?.Keys.Select(k => new SqlResultColumn { Name = k, TypeName = "nvarchar" }).ToList() ?? [],
            Rows = rows.Take(maxRows).ToList(),
            Truncated = truncated,
        };
    }

    private List<Dictionary<string, object?>> CustomerRows()
    {
        string[] cities = ["Brussels", "Antwerp", "Ghent", "Liège", "Namur", "Leuven", "Bruges", "Hasselt"];
        return Enumerable.Range(1, 8).Select(i => new Dictionary<string, object?>
        {
            ["id"] = i,
            ["name"] = $"Customer {i}",
            ["email"] = $"customer{i}@example.com",
            ["city"] = cities[i - 1],
        }).ToList();
    }

    private List<Dictionary<string, object?>> OrderRows()
    {
        string[] statuses = ["new", "processing", "shipped", "delivered"];
        return Enumerable.Range(1, 20).Select(i => new Dictionary<string, object?>
        {
            ["id"] = i,
            ["customer_id"] = (i % 8) + 1,
            ["order_date"] = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc).AddHours(i * 7).ToString("o"),
            ["total"] = Math.Round(19.99m + i * 13.37m, 2),
            ["status"] = statuses[i % statuses.Length],
        }).ToList();
    }

    private List<Dictionary<string, object?>> ProductRows()
    {
        var names = new[] { "Widget", "Gadget", "Sprocket", "Flange", "Coupling", "Housing" };
        var rows = names.Select((n, i) => new Dictionary<string, object?>
        {
            ["id"] = i + 1,
            ["name"] = n,
            ["price"] = Math.Round(9.5m + i * 4.25m, 2),
        }).ToList();
        if (_variant == 0)
            for (var i = 0; i < rows.Count; i++)
                rows[i]["stock"] = 100 - i * 13;
        else
            rows[2]["price"] = 99.99m; // changed row vs. variant 0 for the data-compare demo
        return rows;
    }

    private List<Dictionary<string, object?>> InvoiceRows() =>
        Enumerable.Range(1, 12).Select(i => new Dictionary<string, object?>
        {
            ["id"] = i,
            ["order_id"] = i,
            ["amount"] = Math.Round(19.99m + i * 13.37m, 2),
            ["issued_at"] = new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc).AddDays(i).ToString("o"),
        }).ToList();

    private static List<Dictionary<string, object?>> ReturnRows() =>
    [
        new() { ["id"] = 1, ["order_id"] = 3, ["reason"] = "damaged" },
        new() { ["id"] = 2, ["order_id"] = 7, ["reason"] = "wrong item" },
    ];

    /// <summary>Naive first-keyword check — the demo client doesn't carry the ScriptDom
    /// dependency; the real <c>SqlDatabaseClient</c> uses the full statement classifier.</summary>
    private static bool LooksReadOnly(string sql)
    {
        var trimmed = sql.TrimStart();
        // Strip leading line/block comments so "-- x\nDELETE …" can't sneak past.
        while (true)
        {
            if (trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                var nl = trimmed.IndexOf('\n');
                if (nl < 0) return true; // comment-only input
                trimmed = trimmed[(nl + 1)..].TrimStart();
                continue;
            }
            if (trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                var end = trimmed.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0) return false;
                trimmed = trimmed[(end + 2)..].TrimStart();
                continue;
            }
            break;
        }
        return trimmed.StartsWith("select", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("with", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("print", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("use ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("set ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("declare", StringComparison.OrdinalIgnoreCase);
    }

    private static long Elapsed(long started) =>
        (long)((System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
}
