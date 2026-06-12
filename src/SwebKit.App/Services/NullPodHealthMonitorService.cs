using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.App.Services;

/// <summary>
/// No-op implementation used after <see cref="PodHealthMonitorService"/> was removed.
/// Keeps DashboardPage and legacy AKS sub-components compiling and rendering in an idle state.
/// </summary>
internal sealed class NullPodHealthMonitorService : IPodHealthMonitorService
{
    public bool IsMonitoring => false;
    public IReadOnlyList<string> MonitoredNamespaces => [];
    public IReadOnlyList<PodHealthEvent> RecentEvents => [];

    public event Action<PodHealthEvent>? PodHealthDetected
    {
        add { }
        remove { }
    }

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task AddNamespaceAsync(string ns) => Task.CompletedTask;
    public Task RemoveNamespaceAsync(string ns) => Task.CompletedTask;
    public ValueTask DisposeAsync() => default;
}
