namespace SwebKit.Core.Domain;

public class AksConfig
{
    public string? KubeconfigPath { get; set; }
    public string? KubeconfigContext { get; set; }
    public string DefaultNamespace { get; set; } = string.Empty;
    public List<string> WatchedDeployments { get; set; } = [];
    public int LogBufferSize { get; set; } = 10_000;
    public int CpuBarCeilingMillicores { get; set; } = 500;
    public int MemoryBarCeilingMi { get; set; } = 512;
    public int AutoRefreshIntervalSeconds { get; set; } = 30;

    // Pod Health Monitor
    public List<string> MonitoredNamespaces { get; set; } = [];
    public bool MonitoringEnabled { get; set; }
    public int MonitoringCooldownMinutes { get; set; } = 10;

    public void Validate()
    {
        // KubeconfigPath and KubeconfigContext are optional — the client falls back to the default kubeconfig.
    }
}
