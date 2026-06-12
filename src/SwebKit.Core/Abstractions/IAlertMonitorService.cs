using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IAlertMonitorService : IAsyncDisposable
{
    bool IsMonitoring { get; }
    IReadOnlyList<AlertFiredEvent> RecentAlerts { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    event Action<AlertFiredEvent>? AlertFired;
}
