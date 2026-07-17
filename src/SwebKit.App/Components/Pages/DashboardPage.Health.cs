using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Components.Pages;

/// <remarks>Health tile refresh loop, per-area fetch, and configured-state detection.</remarks>
public partial class DashboardPage
{
    // ── Health state ────────────────────────────────────────────────────────────────
    private bool _sbConfigured, _aksConfigured, _redisConfigured, _releasesConfigured;
    private HealthTileData? _sbData, _aksData, _redisData, _releasesData;
    private string? _sbError, _aksError, _redisError, _releasesError;

    // ── Pod Health ──────────────────────────────────────────────────────────────────
    private readonly List<PodHealthEvent> _monitorAlerts = [];

    private bool _sbLoading;
    private bool _aksLoading;
    private bool _redisLoading;
    private bool _releasesLoading;

    private static readonly TimeSpan HealthRefreshBudget = TimeSpan.FromSeconds(8);

    private readonly SemaphoreSlim _healthRefreshGate = new(1, 1);

    private async Task StartRefreshLoopAsync()
    {
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        try
        {
            while (await _refreshTimer.WaitForNextTickAsync(_cts.Token))
            {
                if (IsLiveRefreshEnabled())
                {
                    RefreshAll();
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void OnPodHealthDetected(PodHealthEvent evt)
    {
        _ = InvokeAsync(() =>
        {
            _monitorAlerts.Insert(0, evt);
            if (_monitorAlerts.Count > 50)
                _monitorAlerts.RemoveAt(_monitorAlerts.Count - 1);
            RequestShellRender();
        });
    }

    private void RefreshAll() => _ = RunRefreshAsync(_cts.Token);

    private async Task RunRefreshAsync(CancellationToken ct)
    {
        try
        {
            await LoadHealthDataAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                Notifications.ShowError("Dashboard refresh failed", ex: ex);
                System.Diagnostics.Debug.WriteLine($"Dashboard refresh failed: {ex}");
            }
        }
    }

    private async Task StopMonitoringNamespaceAsync(string ns)
    {
        await Monitor.RemoveNamespaceAsync(ns);
        RequestShellRender();
    }

    private async Task StopAllMonitoringAsync()
    {
        await Monitor.StopAsync();
        RequestShellRender();
    }

    private async Task LoadHealthDataAsync(CancellationToken ct)
    {
        var enteredGate = false;

        if (DetermineConfiguredState())
        {
            RequestShellRender();
        }

        try
        {
            enteredGate = await _healthRefreshGate.WaitAsync(0, ct);
            if (!enteredGate)
            {
                return;
            }

            var refreshTasks = new List<Task>
            {
                RefreshHealthTileAsync("Service Bus", FetchServiceBusAsync, data => _sbData = data, error => _sbError = error,
                    isLoading => _sbLoading = isLoading, ct),
                RefreshHealthTileAsync("AKS", FetchAksAsync, data => _aksData = data, error => _aksError = error,
                    isLoading => _aksLoading = isLoading, ct),
                RefreshHealthTileAsync("Redis", FetchRedisAsync, data => _redisData = data, error => _redisError = error,
                    isLoading => _redisLoading = isLoading, ct),
                RefreshHealthTileAsync("Pipelines", FetchReleasesAsync, data => _releasesData = data, error => _releasesError = error,
                    isLoading => _releasesLoading = isLoading, ct)
            };

            refreshTasks.AddRange(GetVisibleTilePreferences(DashboardTileRegistry.ServiceBusEntityWatch)
                .Select(preference => RefreshServiceBusEntityWatchAsync(preference, ct)));
            refreshTasks.AddRange(GetVisibleTilePreferences(DashboardTileRegistry.AksNamespaceWatch)
                .Select(preference => RefreshAksNamespaceWatchAsync(preference, ct)));

            await Task.WhenAll(refreshTasks);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            if (enteredGate)
            {
                _healthRefreshGate.Release();
            }
        }
    }

    private async Task RefreshHealthTileAsync(
        string areaName,
        Func<CancellationToken, Task<HealthTileData?>> refresh,
        Action<HealthTileData?> setData,
        Action<string?> setError,
        Action<bool> setLoading,
        CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(HealthRefreshBudget);

        await InvokeAsync(() =>
        {
            setLoading(true);
            RequestTileRender();
        });

        try
        {
            var data = await Task.Run(() => refresh(timeoutCts.Token), timeoutCts.Token);

            if (!ct.IsCancellationRequested)
            {
                await InvokeAsync(() =>
                {
                    setData(data);
                    setError(null);
                });
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            if (!ct.IsCancellationRequested)
            {
                await InvokeAsync(() => setError($"Timed out while refreshing {areaName}."));
            }
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                await InvokeAsync(() => setError(ex.Message));
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                await InvokeAsync(() =>
                {
                    setLoading(false);
                    RequestTileRender();
                });
            }
        }
    }

    private bool DetermineConfiguredState()
    {
        var sbConfigured = AppState.UseDemoData || AppState.ServiceBusNamespaces.Count > 0;
        var aksConfigured = AppState.UseDemoData || !string.IsNullOrWhiteSpace(GetConfiguredAksNamespace());
        var redisConfigured = AppState.UseDemoData || AppState.Config.RedisConfig?.ActiveCache is not null;
        var releasesConfigured = AppState.UseDemoData ||
            (AppState.Config.DevOpsConfig is not null && !string.IsNullOrEmpty(AppState.Config.DevOpsConfig.Organization));

        var changed = sbConfigured != _sbConfigured
            || aksConfigured != _aksConfigured
            || redisConfigured != _redisConfigured
            || releasesConfigured != _releasesConfigured;

        _sbConfigured = sbConfigured;
        _aksConfigured = aksConfigured;
        _redisConfigured = redisConfigured;
        _releasesConfigured = releasesConfigured;
        return changed;
    }

    // ── Service Bus ─────────────────────────────────────────────────────────────────

    private async Task<HealthTileData?> FetchServiceBusAsync(CancellationToken ct)
    {
        if (!_sbConfigured)
        {
            return null;
        }

        long dlq = 0;
        if (AppState.UseDemoData)
        {
            dlq += await SumDlqAsync(DemoServiceBusClient.OrdersDev(), ct);
        }
        else
        {
            foreach (var ns in AppState.ServiceBusNamespaces)
            {
                var connStr = CredentialStore.Get(ns.CredentialKey);
                if (string.IsNullOrWhiteSpace(connStr)) continue;
                var client = SbClientFactory.Create(connStr);
                try
                {
                    dlq += await SumDlqAsync(client, ct);
                }
                finally
                {
                    if (client is IAsyncDisposable d) await d.DisposeAsync();
                }
            }
        }

        return new HealthTileData((int)dlq, "dead-lettered", DateTimeOffset.Now);
    }

    private static async Task<long> SumDlqAsync(IServiceBusClient client, CancellationToken ct)
    {
        var queues = await client.ListQueuesAsync(ct);
        return queues.Sum(queue => queue.Stats?.DeadLetterMessageCount ?? 0);
    }

    // ── AKS ─────────────────────────────────────────────────────────────────────────

    private async Task<HealthTileData?> FetchAksAsync(CancellationToken ct)
    {
        if (!_aksConfigured)
        {
            return null;
        }

        var aksConfig = AppState.Config.AksConfig;
        var ns = GetConfiguredAksNamespace();
        if (string.IsNullOrWhiteSpace(ns))
        {
            return null;
        }

        IAksClient client = AppState.UseDemoData
            ? DemoAksClient
            : AksClientFactory.Create(aksConfig!.KubeconfigContext, aksConfig.KubeconfigPath);

        var pods = await client.GetPodsAsync(ns, null, ct);
        var unhealthy = pods.Count(p => p.Status is not ("Running" or "Succeeded" or "Completed"));
        return new HealthTileData(unhealthy, "unhealthy pods", DateTimeOffset.Now);
    }

    // ── Redis ────────────────────────────────────────────────────────────────────────

    private async Task<HealthTileData?> FetchRedisAsync(CancellationToken ct)
    {
        if (!_redisConfigured)
        {
            return null;
        }

        IRedisClient client = AppState.UseDemoData
            ? DemoRedisClient
            : await RedisClientFactory.CreateAsync(AppState.Config.RedisConfig!.ActiveCache!, ct);

        using (client)
        {
            var scan = await client.ScanKeysAsync("*", 0, 100, ct);
            var infoTasks = scan.Keys.Select(k => client.GetKeyInfoAsync(k, ct));
            var infos = await Task.WhenAll(infoTasks);
            var nearExpiry = infos.Count(i => i.Ttl is { } ttl && ttl < TimeSpan.FromMinutes(5) && ttl > TimeSpan.Zero);
            return new HealthTileData(nearExpiry, "keys expiring < 5m", DateTimeOffset.Now);
        }
    }

    // ── Releases ─────────────────────────────────────────────────────────────────────

    private async Task<HealthTileData?> FetchReleasesAsync(CancellationToken ct)
    {
        if (!_releasesConfigured)
        {
            return null;
        }

        IDevOpsClient client = AppState.UseDemoData
            ? DemoDevOpsClient
            : CreateLiveDevOpsClient();
        var projects = await client.GetProjectsAsync(ct);
        var approvalTasks = projects.Select(p => client.GetPendingApprovalsAsync(p.Name, ct));
        var results = await Task.WhenAll(approvalTasks);
        var total = results.Sum(r => r.Count);
        return new HealthTileData(total, "pending approvals", DateTimeOffset.Now);
    }

    private IDevOpsClient CreateLiveDevOpsClient()
    {
        var config = AppState.Config.DevOpsConfig
            ?? throw new InvalidOperationException("Azure DevOps is not configured.");

        return DevOpsClientFactory.Create(config);
    }

    private string GetConfiguredAksNamespace() => AppState.Config.AksConfig?.DefaultNamespace?.Trim() ?? string.Empty;

    private int GetAttentionCount()
    {
        var count = 0;
        if (_sbData?.Value > 0) count++;
        if (_aksData?.Value > 0) count++;
        if (_redisData?.Value > 0) count++;
        if (_releasesData?.Value > 0) count++;
        count += _serviceBusEntityMetrics.Values.Count(static metric => metric.DeadLetterMessages > 0 ||
!string.IsNullOrWhiteSpace(metric.Error));
        count += _aksNamespaceMetrics.Values.Count(static metric => metric.UnhealthyPods > 0 ||
            (!string.IsNullOrWhiteSpace(metric.Error) && !IsDeploymentPermissionError(metric.Error)));
        return count;
    }
}
