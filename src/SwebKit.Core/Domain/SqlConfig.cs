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
}
