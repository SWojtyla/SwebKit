namespace SwebKit.Core.Domain;

public class AppConfig
{
    public string Name { get; set; } = "Default";
    public bool IsProduction { get; set; }
    public IncidentTimelineConfig IncidentTimeline { get; set; } = new();
    public List<SbEntityLink> ServiceBusEntityLinks { get; set; } = [];
    public AksConfig? AksConfig { get; set; }
    public RedisConfig? RedisConfig { get; set; }
    public List<StorageConfig> StorageAccounts { get; set; } = [];
    public DevOpsConfig? DevOpsConfig { get; set; }
    public ObservabilityConfig? ObservabilityConfig { get; set; }
    public List<FavoriteEntity> FavoriteEntities { get; set; } = [];
    public List<FavoriteResource> FavoriteResources { get; set; } = [];
    public List<SavedWorkspace> SavedWorkspaces { get; set; } = [];
    public WorkspaceTopology Topology { get; set; } = new();
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
