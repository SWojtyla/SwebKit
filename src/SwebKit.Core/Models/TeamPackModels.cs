using SwebKit.Core.Domain;

namespace SwebKit.Core.Models;

/// <summary>
/// A secrets-free, portable slice of a workspace ("team pack") — the curated subset of
/// <see cref="ConfigurationBundle"/> meant to be shared with teammates: workspace maps, saved
/// SQL queries, alert rules, API-client collections/environments, and linked collection roots.
/// Secrets never travel: credential-store references are reduced to a <see cref="CredentialRefs"/>
/// name manifest, <see cref="AuthConfig.CredentialSecret"/> is always stripped, and connection ids
/// (machine-local) degrade to portable hints (<see cref="TeamPackSqlQuery.ConnectionHint"/>).
/// </summary>
public sealed class TeamPack
{
    public const string ExpectedFormat = "swebkit-team-pack";

    public string Format { get; set; } = ExpectedFormat;
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset ExportedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Profile name the pack was exported from — display metadata only.</summary>
    public string? Name { get; set; }

    // Every section is nullable: absent means "not included in this pack" (opt-in export),
    // present-but-empty is a deliberate empty section.

    /// <summary>Workspace maps (Settings → Map component graphs).</summary>
    public List<WorkspaceMap>? Maps { get; set; }

    /// <summary>Saved SQL queries with portable connection hints instead of local connection ids.</summary>
    public List<TeamPackSqlQuery>? SavedSqlQueries { get; set; }

    /// <summary>Monitoring alert rules, minus machine-local runtime state.</summary>
    public List<MonitoringAlertRule>? AlertRules { get; set; }

    /// <summary>API-client collections (credential secrets stripped by construction).</summary>
    public CollectionsStore? CollectionsData { get; set; }

    /// <summary>API-client environments — never carries <see cref="EnvironmentsStore.UiState"/> selections.</summary>
    public EnvironmentsStore? EnvironmentsData { get; set; }

    /// <summary>Linked collection roots ("linked projects") — paths as they were on the source machine.</summary>
    public List<TeamPackLinkedRoot>? LinkedRoots { get; set; }

    /// <summary>Names (never values) of the secrets the importer must re-link — powers the
    /// "N secrets to re-link" report.</summary>
    public List<TeamPackCredentialRef> CredentialRefs { get; set; } = [];

    /// <summary>Human-readable export cautions — e.g. plain-valued environment variables that
    /// might hold real secrets, or linked-root paths that may not exist on the teammate's machine.</summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>A pack section name — the values the <c>?sections=</c> export filter and the
/// importer's section handling agree on.</summary>
public static class TeamPackSections
{
    public const string Maps = "maps";
    public const string SqlQueries = "sqlQueries";
    public const string AlertRules = "alertRules";
    public const string Collections = "collections";
    public const string Environments = "environments";
    public const string LinkedRoots = "linkedRoots";

    public static readonly IReadOnlyList<string> All =
        [Maps, SqlQueries, AlertRules, Collections, Environments, LinkedRoots];
}

/// <summary>
/// A secret *reference* the pack needs re-linked on the teammate's machine — the credential-store
/// key or Key Vault secret name only, never the value.
/// </summary>
public sealed class TeamPackCredentialRef
{
    /// <summary>The credential-store key or Key Vault secret name being referenced.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary><c>"credentialStore"</c>, <c>"azureKeyVault"</c>, or <c>"oauthToken"</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Where the reference lives (e.g. vault name for Key Vault refs, or the
    /// collection/environment the auth config belongs to) — display context only.</summary>
    public string? Hint { get; set; }
}

/// <summary>A saved SQL query in portable form — <see cref="SavedSqlQuery.ConnectionId"/> is
/// machine-local, so the pack carries <see cref="ConnectionHint"/> instead.</summary>
public sealed class TeamPackSqlQuery
{
    public string Name { get; set; } = string.Empty;
    public string? Folder { get; set; }
    public string Sql { get; set; } = string.Empty;

    /// <summary>Server+database of the connection the query was saved against, when known —
    /// the importer rebinds <see cref="SavedSqlQuery.ConnectionId"/> by matching these against
    /// the local profile's SQL connections.</summary>
    public TeamPackConnectionHint? ConnectionHint { get; set; }
}

public sealed class TeamPackConnectionHint
{
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
}

/// <summary>A linked collection root in portable form — the on-disk <see cref="Path"/> travels
/// verbatim; teammates without that path get a conflict note at import and can re-point the root.</summary>
public sealed class TeamPackLinkedRoot
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string? BrunoSyncFolderPath { get; set; }
    public bool BrunoSyncEnabled { get; set; } = true;
}

public enum TeamPackMergeStrategy
{
    /// <summary>Merge into existing state using natural keys (map/environment/collection name,
    /// query name+folder, rule name, linked-root path).</summary>
    Merge,

    /// <summary>Replace each included section wholesale.</summary>
    Replace,
}

public sealed class TeamPackImportOptions
{
    /// <summary>Preview only — computes the report without persisting anything.</summary>
    public bool DryRun { get; set; }

    public TeamPackMergeStrategy Strategy { get; set; } = TeamPackMergeStrategy.Merge;
}

/// <summary>
/// The import/preview report: counts per outcome, human-readable conflicts, the secret references
/// that still need re-linking, and export-side warnings carried through so the dialog shows them.
/// </summary>
public sealed class TeamPackImportResult
{
    public bool DryRun { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<string> Conflicts { get; set; } = [];
    public List<TeamPackCredentialRef> RequiredCredentialRefs { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
