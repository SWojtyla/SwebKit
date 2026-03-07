namespace SwebKit.Core.Domain;

public class ObservabilityConfig
{
    public ObservabilityProviderType Provider { get; set; } = ObservabilityProviderType.AppInsights;

    // AppInsights / Azure Monitor
    public string? WorkspaceId { get; set; }
    public string? ApplicationId { get; set; }
    public string CredentialMode { get; set; } = "DefaultAzureCredential";
    public string? CredentialRef { get; set; }

    // OTLP
    public string? OtlpEndpoint { get; set; }
    public Dictionary<string, string> OtlpHeaders { get; set; } = [];
    public Dictionary<string, string> ResourceAttributes { get; set; } = [];
}
