using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Observability;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Routes Log Analytics workspace discovery and queries to the real ARM/Azure Monitor service
/// or the fixed demo set based on the current demo-mode flag — the workspace analogue of
/// <see cref="ObservabilityResourceDiscoverySelector"/>.
/// </summary>
public sealed class LogAnalyticsWorkspaceSelector : ILogAnalyticsWorkspaceService
{
    private readonly AppStateService _appState;
    private readonly AzureLogAnalyticsWorkspaceService _real;
    private readonly DemoLogAnalyticsWorkspaceService _demo = new();

    public LogAnalyticsWorkspaceSelector(AppStateService appState, AzureLogAnalyticsWorkspaceService real)
    {
        _appState = appState;
        _real = real;
    }

    private ILogAnalyticsWorkspaceService Inner => _appState.UseDemoData ? _demo : _real;

    public Task<IReadOnlyList<LogAnalyticsWorkspaceInfo>> FindWorkspacesAsync(string? nameFilter = null, CancellationToken ct = default) =>
        Inner.FindWorkspacesAsync(nameFilter, ct);

    public Task<LogQueryResult> RunWorkspaceQueryAsync(string customerId, string query, TimeRange range, int maxRows, CancellationToken ct = default) =>
        Inner.RunWorkspaceQueryAsync(customerId, query, range, maxRows, ct);
}
