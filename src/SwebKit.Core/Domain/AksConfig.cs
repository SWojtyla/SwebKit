namespace SwebKit.Core.Domain;

public class AksConfig
{
    public string? KubeconfigPath { get; set; }
    public string? KubeconfigContext { get; set; }
    public string DefaultNamespace { get; set; } = "default";
    public List<string> WatchedDeployments { get; set; } = [];
}
