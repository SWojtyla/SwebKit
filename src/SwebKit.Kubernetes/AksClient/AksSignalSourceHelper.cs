using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Kubernetes.AksClient;

/// <summary>
/// Retained for any future direct AKS client creation outside the monitoring pool.
/// Monitoring signal sources now use <see cref="IMonitoringConnectionPool"/> instead.
/// </summary>
internal static class AksSignalSourceHelper
{
    internal static IAksClient? CreateClient(AppStateService appState)
    {
        if (appState.UseDemoData)
            return new DemoAksClient();
        var cfg = appState.Config.AksConfig;
        return cfg is null ? null : new KubernetesAksClient(cfg.KubeconfigContext, cfg.KubeconfigPath);
    }
}
