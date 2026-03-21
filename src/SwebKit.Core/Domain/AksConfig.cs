namespace SwebKit.Core.Domain;

public class AksConfig
{
    public string? KubeconfigPath { get; set; }
    public string? KubeconfigContext { get; set; }
    public string DefaultNamespace { get; set; } = "default";
    public List<string> WatchedDeployments { get; set; } = [];

    public void Validate()
    {
        // KubeconfigPath and KubeconfigContext are optional — the client falls back to the default kubeconfig.
        if (string.IsNullOrWhiteSpace(DefaultNamespace))
            throw new InvalidOperationException($"{nameof(AksConfig)}.{nameof(DefaultNamespace)} is required.");
    }
}
