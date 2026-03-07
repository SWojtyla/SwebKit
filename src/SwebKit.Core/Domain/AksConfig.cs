namespace SwebKit.Core.Domain;

public class AksConfig
{
    public string? KubeconfigContext { get; set; }
    public string? ExplicitClusterUrl { get; set; }
    public string? CredentialRef { get; set; }
    public string DefaultNamespace { get; set; } = "default";
    public List<string> WatchedDeployments { get; set; } = [];
}
