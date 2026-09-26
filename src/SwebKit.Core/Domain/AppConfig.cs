namespace SwebKit.Core.Domain;

public class AppConfig
{
    public string Name { get; set; } = "Default";
    public bool IsProduction { get; set; }
    public List<SbEntityLink> ServiceBusEntityLinks { get; set; } = [];
    public AksConfig? AksConfig { get; set; }
    public RedisConfig? RedisConfig { get; set; }
    public SqlConfig? SqlConfig { get; set; }
    public List<StorageConfig> StorageAccounts { get; set; } = [];
    public ObservabilityConfig? ObservabilityConfig { get; set; }
    public List<FavoriteEntity> FavoriteEntities { get; set; } = [];
    public List<FavoriteResource> FavoriteResources { get; set; } = [];
    public List<SavedWorkspace> SavedWorkspaces { get; set; } = [];

    /// <summary>The user-curated workspace maps (Settings → Map) — one named component graph per
    /// project/environment. Consumers should read <see cref="EffectiveMaps"/> rather than this list
    /// directly so a not-yet-migrated legacy <see cref="Topology"/> still counts.</summary>
    public List<WorkspaceMap> Maps { get; set; } = [];

    /// <summary>Legacy single-map storage from before <see cref="Maps"/> existed. Profiles still
    /// carrying nodes/relationships here get migrated into <see cref="Maps"/> by profile
    /// normalization; the property remains so those documents keep deserializing.</summary>
    public WorkspaceTopology Topology { get; set; } = new();

    /// <summary>Every map the workspace effectively has: <see cref="Maps"/> plus — when it still
    /// holds content — the legacy <see cref="Topology"/> wrapped as a pseudo-map. Normalization
    /// drains the legacy graph into <see cref="Maps"/> on load/save, but in-memory writes (and a
    /// profile PUT that raced normalization) can leave nodes there; reading through this keeps
    /// every consumer honest. The wrapper's stable <c>"legacy"</c> id means two calls in one
    /// operation still agree on which map a node belongs to.</summary>
    public IEnumerable<WorkspaceMap> EffectiveMaps() =>
        Topology.Nodes.Count > 0 || Topology.Relationships.Count > 0
            ? Maps.Append(new WorkspaceMap
            {
                Id = "legacy",
                Name = string.IsNullOrWhiteSpace(Name) ? "Default" : Name,
                Nodes = Topology.Nodes,
                Relationships = Topology.Relationships,
            })
            : Maps;
    public Dictionary<string, FilterState> LastUsedFilters { get; set; } = [];
    /// <summary>Azure Key Vault URL for resolving <c>AzureKeyVault</c> environment variables (e.g. https://my-vault.vault.azure.net/). Optional.</summary>
    [Obsolete("Use KeyVaults instead. Kept for backward-compatible deserialization of existing profiles.")]
    public string? KeyVaultUrl { get; set; }

    /// <summary>
    /// Named list of Azure Key Vaults available for environment variable resolution.
    /// Each entry has a friendly <see cref="KeyVaultEntry.Name"/> (shown in the UI) and a vault <see cref="KeyVaultEntry.Url"/>.
    /// </summary>
    public List<KeyVaultEntry> KeyVaults { get; set; } = [];
}

/// <summary>A named reference to an Azure Key Vault used for environment variable secret resolution.</summary>
public sealed class KeyVaultEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Friendly name shown in the vault picker (e.g. "Production KV").</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Full vault URL, e.g. https://my-vault.vault.azure.net/</summary>
    public string Url { get; set; } = string.Empty;
}
