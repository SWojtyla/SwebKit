namespace SwebKit.Core.Domain;

/// <summary>Global (not per-project) Service Bus namespace, added by connection string.</summary>
public class ServiceBusNamespace
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User-friendly label, auto-derived from the hostname short name.</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>e.g. sb-dev-shared-sb-weu.servicebus.windows.net</summary>
    public string FullyQualifiedNamespace { get; set; } = string.Empty;

    /// <summary>Key used to retrieve the connection string from ICredentialStore.</summary>
    public string CredentialKey { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
