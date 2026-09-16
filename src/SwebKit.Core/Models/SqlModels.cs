namespace SwebKit.Core.Models;

/// <summary>A database on a connected SQL server.</summary>
public sealed record SqlDatabaseInfo(string Name, string State);

/// <summary>A column inside a table/view in the schema model.</summary>
public sealed class SqlColumnInfo
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
}

/// <summary>An index on a table/view (name + uniqueness + column list — enough for a compare
/// diff; full index definitions are deliberately out of scope).</summary>
public sealed class SqlIndexInfo
{
    public string Name { get; set; } = string.Empty;
    public bool IsUnique { get; set; }
    public bool IsPrimaryKey { get; set; }
    public List<string> Columns { get; set; } = [];
}

/// <summary>A foreign key on a table (name + referenced schema.object — enough for compare).</summary>
public sealed class SqlForeignKeyInfo
{
    public string Name { get; set; } = string.Empty;
    public string ReferencedObject { get; set; } = string.Empty;
}

/// <summary>A table or view inside one schema.</summary>
public sealed class SqlObjectInfo
{
    public string Name { get; set; } = string.Empty;
    /// <summary>"table" or "view".</summary>
    public string Kind { get; set; } = "table";
    public List<SqlColumnInfo> Columns { get; set; } = [];
    public List<SqlIndexInfo> Indexes { get; set; } = [];
    public List<SqlForeignKeyInfo> ForeignKeys { get; set; } = [];
}

/// <summary>One schema and the objects it contains.</summary>
public sealed class SqlSchemaGroup
{
    public string Name { get; set; } = string.Empty;
    public List<SqlObjectInfo> Objects { get; set; } = [];
}

/// <summary>The browsable schema tree for one database: schemas → objects → columns.</summary>
public sealed class SqlSchemaModel
{
    public string? Database { get; set; }
    public List<SqlSchemaGroup> Schemas { get; set; } = [];
}

/// <summary>A result column of an executed query.</summary>
public sealed class SqlResultColumn
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
}

/// <summary>
/// Windowed result of an executed query or a table browse. Rows are name→value maps so the
/// frontend can render a grid without positional bookkeeping. <see cref="Truncated"/> is true
/// when more rows existed than <c>maxRows</c> allowed.
/// </summary>
public sealed class SqlQueryResult
{
    public List<SqlResultColumn> Columns { get; set; } = [];
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
    public bool Truncated { get; set; }
    public long ElapsedMs { get; set; }
    /// <summary>Rows affected by the statement, when the driver reports it (writes / no result set).</summary>
    public int RowsAffected { get; set; } = -1;
}

// ── Data compare ────────────────────────────────────────────────────────────

/// <summary>One column's differing values inside a changed row.</summary>
public sealed class SqlColumnDiff
{
    public string Column { get; set; } = string.Empty;
    public object? SourceValue { get; set; }
    public object? TargetValue { get; set; }
}

/// <summary>A row present on both sides (same key) whose non-key column values differ.</summary>
public sealed class SqlChangedRow
{
    public Dictionary<string, object?> Key { get; set; } = [];
    public List<SqlColumnDiff> Diffs { get; set; } = [];
}

/// <summary>
/// Result of a row-level data compare between two connections/databases. Counts are always exact;
/// the row payloads are capped at <c>maxDiffRows</c> per bucket when <see cref="Truncated"/>.
/// </summary>
public sealed class SqlDataCompareResult
{
    public List<Dictionary<string, object?>> OnlyInSource { get; set; } = [];
    public List<Dictionary<string, object?>> OnlyInTarget { get; set; } = [];
    public List<SqlChangedRow> Changed { get; set; } = [];
    public int TotalOnlyInSource { get; set; }
    public int TotalOnlyInTarget { get; set; }
    public int TotalChanged { get; set; }
    public bool Truncated { get; set; }
    /// <summary>Columns skipped because they exist on only one side.</summary>
    public List<string> SchemaWarnings { get; set; } = [];
}

// ── Schema compare ──────────────────────────────────────────────────────────

/// <summary>One differing property inside a schema-compare object diff.</summary>
public sealed class SqlPropertyDiff
{
    public string Property { get; set; } = string.Empty;
    public string? SourceValue { get; set; }
    public string? TargetValue { get; set; }
}

/// <summary>An object that exists on only one side, or whose properties differ.</summary>
public sealed class SqlObjectDiff
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "table";
    public List<SqlPropertyDiff> Diffs { get; set; } = [];
}

/// <summary>Result of a catalog compare between two databases — report only, no sync scripts.</summary>
public sealed class SqlSchemaCompareResult
{
    public List<SqlObjectDiff> OnlyInSource { get; set; } = [];
    public List<SqlObjectDiff> OnlyInTarget { get; set; } = [];
    public List<SqlObjectDiff> Differing { get; set; } = [];
}

// ── Discovery / health ──────────────────────────────────────────────────────

/// <summary>A SQL server found via ARM discovery (Entra).</summary>
public sealed class SqlDiscoveredServer
{
    public string ServerFqdn { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public List<string> Databases { get; set; } = [];
}

/// <summary>Shallow health snapshot used by the agent's check_sql_health tool and the
/// workspace investigation walker — deliberately not a DBA dashboard.</summary>
public sealed class SqlHealthReport
{
    public bool Connected { get; set; }
    public string? ServerName { get; set; }
    public string? Version { get; set; }
    public string? DatabaseState { get; set; }
    public int BlockingSessionCount { get; set; }
    public List<string> Notes { get; set; } = [];
}

/// <summary>Thrown when the read-only guard rejects a statement on a write-disabled connection
/// (or from an agent read tool). Carries a user-safe reason — endpoints surface it as a 400.</summary>
public sealed class SqlWriteGuardException : InvalidOperationException
{
    public SqlWriteGuardException(string reason) : base(reason) { }
}
