namespace SwebKit.Core.Domain;

public class ServiceBusConfig
{
    public required string NamespaceHostname { get; set; }
    public SbAuthMode AuthMode { get; set; } = SbAuthMode.DefaultAzureCredential;
    public string? CredentialRef { get; set; }
    public List<string> FavoriteQueues { get; set; } = [];
    public List<string> FavoriteTopics { get; set; } = [];

    public string FullyQualifiedNamespace =>
        NamespaceHostname.Contains('.') ? NamespaceHostname : $"{NamespaceHostname}.servicebus.windows.net";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(NamespaceHostname))
            throw new InvalidOperationException($"{nameof(ServiceBusConfig)}.{nameof(NamespaceHostname)} is required.");
    }
}
