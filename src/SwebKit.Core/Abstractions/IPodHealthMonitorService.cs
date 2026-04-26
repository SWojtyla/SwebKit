using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IPodHealthMonitorService : IAsyncDisposable
{
    bool IsMonitoring { get; }
    IReadOnlyList<string> MonitoredNamespaces { get; }

    /// <summary>Ring buffer of the last 100 detected events.</summary>
    IReadOnlyList<PodHealthEvent> RecentEvents { get; }

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();

    Task AddNamespaceAsync(string ns);
    Task RemoveNamespaceAsync(string ns);

    event Action? MonitoringStateChanged;
    event Action<PodHealthEvent>? PodHealthDetected;
}
