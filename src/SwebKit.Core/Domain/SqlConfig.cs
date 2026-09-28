using System.Text.Json.Serialization;

namespace SwebKit.Core.Domain;

/// <summary>
/// Top-level SQL Server/Azure SQL configuration for an environment.
/// Entra-only auth by design: no credential fields exist on <see cref="SqlConnectionEntry"/> —
/// connections acquire a <c>https://database.windows.net/.default</c> token through the shared
/// <see cref="Services.AzureCredentialFactory"/>, matching the Storage/Service Bus/Redis-Entra
/// pattern (see docs/pitfalls/azure-sdk.md AZ-4).
/// </summary>
public class SqlConfig
{
    /// <summary>Named connection entries for this configuration.</summary>
    public List<SqlConnectionEntry> Connections { get; set; } = [];

    /// <summary>Id of the currently active connection.</summary>
    public string? ActiveConnectionId { get; set; }

    /// <summary>Returns the currently active connection entry, or null.</summary>
    [JsonIgnore]
    public SqlConnectionEntry? ActiveConnection =>
        Connections.FirstOrDefault(c => c.Id == ActiveConnectionId) ?? Connections.FirstOrDefault();

    public void Validate()
    {
        if (Connections.Count == 0)
            throw new InvalidOperationException($"{nameof(SqlConfig)} must have at least one connection entry.");
        foreach (var entry in Connections)
        {
            if (string.IsNullOrWhiteSpace(entry.Server))
                throw new InvalidOperationException($"{nameof(SqlConnectionEntry)}.{nameof(SqlConnectionEntry.Server)} is required for connection '{entry.DisplayName}'.");
        }
    }
}

/// <summary>
/// A single named SQL Server/Azure SQL connection within an environment.
/// </summary>
public class SqlConnectionEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string DisplayName { get; set; } = "Database";

    /// <summary>Server FQDN or hostname, e.g. myserver.database.windows.net or localhost\SQLEXPRESS.
    /// No protocol prefix, no port required (1433 is the default).</summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>Initial database. Empty means connect to the server and let the user pick
    /// per-session — the schema/query endpoints take a <c>database</c> override.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>Gate for mutating statements from the query editor and the agent's
    /// <c>propose_execute_sql</c> executor. Default false = read-only, enforced by the
    /// ScriptDom statement classifier in SwebKit.Sql (default-deny: only SELECT/PRINT/USE/SET/
    /// DECLARE pass). Mirrors <see cref="StorageConfig.AllowMutations"/>.</summary>
    public bool AllowWrites { get; set; }

    /// <summary>Inactive connections stay configured but are skipped by the page pickers.</summary>
    public bool Active { get; set; } = true;

    // ── Optional ARM identity (access-awareness Phase 3a) ─────────────────────
    // Populated automatically when the connection is added from ARM discovery; editable
    // by hand in settings. Never inferred from the server hostname — a connection without
    // these simply can't produce a scoped access-request artifact.

    /// <summary>Azure subscription GUID hosting the server, when known.</summary>
    public string? SubscriptionId { get; set; }

    /// <summary>Resource group hosting the server, when known.</summary>
    public string? ResourceGroup { get; set; }

    /// <summary>Full ARM resource id of the server
    /// (/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Sql/servers/{name}),
    /// when known. Takes precedence over composing from <see cref="SubscriptionId"/> +
    /// <see cref="ResourceGroup"/>.</summary>
    public string? ResourceId { get; set; }

    /// <summary>
    /// User-declared object names merged into the schema tree when the catalog can't be
    /// browsed (VIEW DEFINITION denied) — a locked-down identity may still hold SELECT/EXECUTE
    /// on specific objects it knows by name. Entries are <c>"schema.name"</c> for queryable
    /// objects (views/tables) and <c>"exec:schema.name"</c> for runnable procedures; see
    /// <see cref="SqlDeclaredObject"/> for the grammar. The setter normalizes (trims, dedupes,
    /// drops malformed entries) so the list can never carry a value that breaks identifier
    /// quoting later — this doubles as the save-time validation.
    /// </summary>
    public List<string> DeclaredObjects
    {
        get => _declaredObjects;
        set => _declaredObjects = SqlDeclaredObject.NormalizeList(value);
    }
    private List<string> _declaredObjects = [];
}

/// <summary>
/// A parsed user-declared object reference: <c>"schema.name"</c> (a queryable table/view)
/// or <c>"exec:schema.name"</c> (a runnable stored procedure). The grammar is deliberately
/// narrower than T-SQL's — bare two-part identifiers only, no bracket quoting — because a
/// declared name only ever reaches the server through <c>SqlDatabaseClient</c>'s bracket
/// quoting, and a strict grammar keeps the canonical persisted form unambiguous.
/// </summary>
public readonly record struct SqlDeclaredObject
{
    /// <summary>Prefix marking a declared entry as an executable procedure.</summary>
    public const string ProcedurePrefix = "exec:";

    /// <summary>Max identifier length SQL Server accepts.</summary>
    private const int MaxIdentifierLength = 128;

    public string Schema { get; }
    public string Name { get; }
    /// <summary>True when the entry is a runnable procedure (<c>exec:</c> prefix) —
    /// procedures are listed but never introspected with SELECT TOP 0.</summary>
    public bool IsProcedure { get; }

    private SqlDeclaredObject(string schema, string name, bool isProcedure)
    {
        Schema = schema;
        Name = name;
        IsProcedure = isProcedure;
    }

    /// <summary>The canonical persisted/wire form: <c>schema.name</c> or
    /// <c>exec:schema.name</c>.</summary>
    public override string ToString() =>
        IsProcedure ? $"{ProcedurePrefix}{Schema}.{Name}" : $"{Schema}.{Name}";

    /// <summary>
    /// A single identifier part is valid when it matches <c>[A-Za-z_][A-Za-z0-9_@$#]*</c>
    /// and fits the 128-char limit — the regular-identifier subset of T-SQL. Used on save
    /// (via <see cref="NormalizeList"/>) and by the settings UI to validate input.
    /// </summary>
    public static bool IsValidIdentifier(string? part)
    {
        if (string.IsNullOrEmpty(part) || part.Length > MaxIdentifierLength)
            return false;
        if (!(char.IsLetter(part[0]) || part[0] == '_'))
            return false;
        foreach (var c in part)
        {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '@' or '$' or '#'))
                return false;
        }
        return true;
    }

    /// <summary>Parses <c>"schema.name"</c> or <c>"exec:schema.name"</c>; false for
    /// anything else (missing/extra parts, invalid identifiers, empty segments).</summary>
    public static bool TryParse(string? entry, out SqlDeclaredObject result)
    {
        result = default;
        var text = entry?.Trim();
        if (string.IsNullOrEmpty(text))
            return false;

        var isProcedure = text.StartsWith(ProcedurePrefix, StringComparison.OrdinalIgnoreCase);
        if (isProcedure)
            text = text[ProcedurePrefix.Length..].Trim();

        var dot = text.IndexOf('.');
        if (dot <= 0 || dot != text.LastIndexOf('.') || dot == text.Length - 1)
            return false;

        var schema = text[..dot];
        var name = text[(dot + 1)..];
        if (!IsValidIdentifier(schema) || !IsValidIdentifier(name))
            return false;

        result = new SqlDeclaredObject(schema, name, isProcedure);
        return true;
    }

    /// <summary>
    /// Trims, canonicalizes and dedupes (case-insensitive, first-wins) a raw declared list —
    /// the normalization applied on assignment. Malformed entries are dropped rather than
    /// persisted: the list lands in profiles.json and later inside quoted identifiers.
    /// </summary>
    public static List<string> NormalizeList(IEnumerable<string>? entries)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in entries ?? [])
        {
            if (TryParse(raw, out var parsed) && seen.Add(parsed.ToString()))
                normalized.Add(parsed.ToString());
        }
        return normalized;
    }
}
