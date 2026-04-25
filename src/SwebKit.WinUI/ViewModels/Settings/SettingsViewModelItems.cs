using SwebKit.Core.Domain;

namespace SwebKit.WinUI.ViewModels.Settings;

public sealed record SettingsSectionOption(string Key, string Title, string Description);

public sealed record ServiceBusNamespaceStatusItem(ServiceBusNamespace Namespace, bool HasCredential)
{
    public string Alias => string.IsNullOrWhiteSpace(Namespace.Alias) ? Namespace.FullyQualifiedNamespace : Namespace.Alias;

    public string FullyQualifiedNamespace => Namespace.FullyQualifiedNamespace;

    public string CredentialKey => Namespace.CredentialKey;

    public string CredentialSummary => HasCredential
        ? "Credential reference is available in Windows Credential Manager."
        : "Credential reference is missing from Windows Credential Manager.";

    public string CredentialStateLabel => HasCredential ? "Credential ready" : "Credential missing";
}

public sealed record ServiceBusPinnedEntityItem(SbEntityLink Link, string NamespaceLabel)
{
    public string EntityPath => Link.EntityPath;

    public string DisplayPath => $"{NamespaceLabel} / {Link.EntityPath}";
}

public sealed record RedisCacheDisplayItem(RedisCacheEntry Cache)
{
    public string DisplayName => Cache.DisplayName;

    public string DatabaseLabel => $"DB{Cache.Database}";
}

public sealed record StorageAccountDisplayItem(StorageConfig Config)
{
    public string DisplayName => Config.DisplayName;

    public string AccountName => Config.AccountName;

    public string AuthLabel => Config.UseAad ? "Azure AD" : "Credential ref";

    public string CredentialSummary => Config.UseAad
        ? "Uses DefaultAzureCredential with the storage account name."
        : string.IsNullOrWhiteSpace(Config.ConnectionStringRef)
            ? "Connection string reference is missing."
            : $"Credential key: {Config.ConnectionStringRef}";
}