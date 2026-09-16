namespace SwebKit.Core.Domain;

/// <summary>A saved SQL query — user-named snippet tied to a connection (or shared when
/// <see cref="ConnectionId"/> is null). Persisted in <c>sql-queries.json</c>.</summary>
public sealed class SavedSqlQuery
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string? Folder { get; set; }
    public string Sql { get; set; } = string.Empty;
    public string? ConnectionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>One executed-statement history record. Written on every /query execution,
/// success or failure.</summary>
public sealed class SqlHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Sql { get; set; } = string.Empty;
    public string? ConnectionId { get; set; }
    public string? Database { get; set; }
    public DateTimeOffset ExecutedAt { get; set; } = DateTimeOffset.UtcNow;
    public long ElapsedMs { get; set; }
    public int RowCount { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
}

/// <summary>Root of <c>sql-queries.json</c>: saved queries plus the capped execution history.</summary>
public sealed class SqlQueriesStore
{
    public List<SavedSqlQuery> Queries { get; set; } = [];
    public List<SqlHistoryEntry> History { get; set; } = [];
}
