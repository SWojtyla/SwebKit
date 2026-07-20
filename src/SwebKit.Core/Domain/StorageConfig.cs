namespace SwebKit.Core.Domain;

public sealed class StorageConfig
{
    /// <summary>Stable identifier. Auto-generated if not set.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Human-readable label for the account in the UI.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Storage account name. Required when UseAad = true.
    /// Used to build the blob service endpoint: https://{AccountName}.blob.core.windows.net
    /// </summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>
    /// Key in ICredentialStore that maps to the full connection string.
    /// Required when UseAad = false. Ignored when UseAad = true.
    /// </summary>
    public string? ConnectionStringRef { get; set; }

    /// <summary>
    /// When true, authenticate with DefaultAzureCredential using AccountName.
    /// When false, authenticate with the connection string from ConnectionStringRef.
    /// </summary>
    public bool UseAad { get; set; }

    /// <summary>
    /// When true, mutation operations (upload, copy, metadata update, restore, undelete) are permitted.
    /// Defaults to false so all existing environments remain read-only without configuration changes.
    /// </summary>
    public bool AllowMutations { get; set; }
}
