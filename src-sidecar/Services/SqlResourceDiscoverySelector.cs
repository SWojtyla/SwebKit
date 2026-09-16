using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Sql;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Routes SQL server discovery to the real ARM scanner or the fixed demo set based on the
/// current demo-mode flag — same shape as <see cref="ObservabilityResourceDiscoverySelector"/>.
/// </summary>
public sealed class SqlResourceDiscoverySelector : ISqlResourceDiscovery
{
    private readonly AppStateService _appState;
    private readonly SqlServerDiscoveryService _real;
    private readonly DemoSqlResourceDiscovery _demo = new();

    public SqlResourceDiscoverySelector(AppStateService appState, SqlServerDiscoveryService real)
    {
        _appState = appState;
        _real = real;
    }

    public IAsyncEnumerable<SqlDiscoveredServer> DiscoverServersAsync(CancellationToken ct = default) =>
        _appState.UseDemoData ? _demo.DiscoverServersAsync(ct) : _real.DiscoverServersAsync(ct);

    public void InvalidateCache() => _real.InvalidateCache();
}
