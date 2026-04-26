using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Kubernetes.AksClient;

namespace SwebKit.WinUI.Services;

public sealed class PodHealthMonitorService : IPodHealthMonitorService
{
    private readonly AppStateService _appState;
    private readonly IAppEventBus _eventBus;
    private readonly INotificationService _notifications;
    private readonly IWindowsNotificationService _toastService;
    private readonly IConnectionStateService _connectionState;
    private readonly UiStateRepository _uiState;
    private readonly ILogger<PodHealthMonitorService> _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<PodHealthEvent> _recentEvents = [];
    private readonly List<string> _monitoredNamespaces = [];
    private readonly Dictionary<string, Dictionary<string, PodSnapshot>?> _namespaceSnapshots = [];
    private readonly Dictionary<string, DateTimeOffset> _cooldowns = [];

    private volatile bool _isMonitoring;
    private CancellationTokenSource? _cts;
    private PeriodicTimer? _timer;
    private Task? _loopTask;

    private IAksClient? _aksClient;
    private string? _lastKubeconfigContext;
    private bool _lastWasDemo;
    private bool _disposed;

    public PodHealthMonitorService(
        AppStateService appState,
        IAppEventBus eventBus,
        INotificationService notifications,
        ILogger<PodHealthMonitorService> logger,
        IWindowsNotificationService toastService,
        IConnectionStateService connectionState,
        UiStateRepository uiState)
    {
        _appState = appState;
        _eventBus = eventBus;
        _notifications = notifications;
        _logger = logger;
        _toastService = toastService;
        _connectionState = connectionState;
        _uiState = uiState;

        _appState.Initialized += OnAppInitialized;
    }

    public bool IsMonitoring => _isMonitoring;

    public IReadOnlyList<string> MonitoredNamespaces => _monitoredNamespaces;

    public IReadOnlyList<PodHealthEvent> RecentEvents
    {
        get
        {
            _lock.Wait();
            try
            {
                return _recentEvents.ToList();
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    public event Action? MonitoringStateChanged;

    public event Action<PodHealthEvent>? PodHealthDetected;

    private void OnAppInitialized()
    {
        var aksConfig = _appState.Config.AksConfig;

        if (_appState.UseDemoData && aksConfig is null)
        {
            foreach (var ns in _uiState.State.DemoMonitoredNamespaces)
            {
                if (!_monitoredNamespaces.Contains(ns, StringComparer.Ordinal))
                {
                    _monitoredNamespaces.Add(ns);
                }
            }

            if (_uiState.State.DemoMonitoringEnabled && _monitoredNamespaces.Count > 0)
            {
                _ = StartAsync();
            }
        }
        else
        {
            if (aksConfig is not null)
            {
                foreach (var ns in aksConfig.MonitoredNamespaces)
                {
                    if (!_monitoredNamespaces.Contains(ns, StringComparer.Ordinal))
                    {
                        _monitoredNamespaces.Add(ns);
                    }
                }
            }

            if (aksConfig?.MonitoringEnabled == true && _monitoredNamespaces.Count > 0)
            {
                _ = StartAsync();
            }
        }

        MonitoringStateChanged?.Invoke();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_isMonitoring)
        {
            return;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_isMonitoring)
            {
                return;
            }

            foreach (var ns in _monitoredNamespaces)
            {
                _namespaceSnapshots.TryAdd(ns, null);
            }

            _isMonitoring = true;
        }
        finally
        {
            _lock.Release();
        }

        if (_appState.UseDemoData)
        {
            _uiState.State.DemoMonitoringEnabled = true;
            _ = _uiState.SaveAsync();
        }
        else
        {
            var aksConfig = _appState.Config.AksConfig;
            if (aksConfig is not null && !aksConfig.MonitoringEnabled)
            {
                aksConfig.MonitoringEnabled = true;
                await _appState.SaveConfigAsync();
            }
        }

        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(120));
        _loopTask = Task.Run(() => PollingLoopAsync(_cts.Token));
        MonitoringStateChanged?.Invoke();
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _timer?.Dispose();
        _timer = null;

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            _loopTask = null;
        }

        await _lock.WaitAsync();
        try
        {
            _namespaceSnapshots.Clear();
            _isMonitoring = false;
        }
        finally
        {
            _lock.Release();
        }

        if (_appState.UseDemoData)
        {
            _uiState.State.DemoMonitoringEnabled = false;
            _ = _uiState.SaveAsync();
        }
        else
        {
            var aksConfig = _appState.Config.AksConfig;
            if (aksConfig is not null && aksConfig.MonitoringEnabled)
            {
                aksConfig.MonitoringEnabled = false;
                await _appState.SaveConfigAsync();
            }
        }

        MonitoringStateChanged?.Invoke();
    }

    public async Task AddNamespaceAsync(string ns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ns);

        await _lock.WaitAsync();
        try
        {
            if (!_monitoredNamespaces.Contains(ns, StringComparer.Ordinal))
            {
                _monitoredNamespaces.Add(ns);
            }

            var aksConfig = _appState.Config.AksConfig;
            if (aksConfig is not null && !aksConfig.MonitoredNamespaces.Contains(ns, StringComparer.Ordinal))
            {
                aksConfig.MonitoredNamespaces.Add(ns);
            }

            _namespaceSnapshots.TryAdd(ns, null);
        }
        finally
        {
            _lock.Release();
        }

        if (_appState.UseDemoData)
        {
            if (!_uiState.State.DemoMonitoredNamespaces.Contains(ns, StringComparer.Ordinal))
            {
                _uiState.State.DemoMonitoredNamespaces.Add(ns);
            }

            await _uiState.SaveAsync();
        }
        else if (_appState.Config.AksConfig is not null)
        {
            await _appState.SaveConfigAsync();
        }

        if (_isMonitoring)
        {
            await TakeBaselineAsync(ns);
        }

        MonitoringStateChanged?.Invoke();
    }

    public async Task RemoveNamespaceAsync(string ns)
    {
        await _lock.WaitAsync();
        try
        {
            _monitoredNamespaces.Remove(ns);
            _appState.Config.AksConfig?.MonitoredNamespaces.Remove(ns);
            _namespaceSnapshots.Remove(ns);
        }
        finally
        {
            _lock.Release();
        }

        if (_appState.UseDemoData)
        {
            _uiState.State.DemoMonitoredNamespaces.Remove(ns);
            await _uiState.SaveAsync();
        }
        else if (_appState.Config.AksConfig is not null)
        {
            await _appState.SaveConfigAsync();
        }

        MonitoringStateChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _appState.Initialized -= OnAppInitialized;

        await StopAsync();
        _cts?.Dispose();
        _lock.Dispose();

        if (_aksClient is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
    }

    private async Task PollingLoopAsync(CancellationToken ct)
    {
        try
        {
            await PollAllNamespacesAsync(ct);

            while (await _timer!.WaitForNextTickAsync(ct))
            {
                await PollAllNamespacesAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("PodHealthMonitorService polling loop cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in PodHealthMonitorService polling loop.");
        }
    }

    private async Task PollAllNamespacesAsync(CancellationToken ct)
    {
        List<string> namespaces;
        IAksClient? client;

        await _lock.WaitAsync(ct);
        try
        {
            namespaces = [.. _namespaceSnapshots.Keys];
            client = GetOrCreateClient();
        }
        finally
        {
            _lock.Release();
        }

        if (namespaces.Count == 0 || client is null)
        {
            if (client is null)
            {
                _logger.LogWarning("AKS client unavailable (no AksConfig and not in demo mode) - skipping pod health poll.");
            }

            return;
        }

        bool connected;
        try
        {
            connected = await client.TestConnectionAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AKS connection test threw - skipping pod health poll.");
            _connectionState.SetError("aks", ex.Message);
            await ClearAllSnapshotsAsync(ct);
            return;
        }

        if (!connected)
        {
            _logger.LogWarning("AKS connection test failed - skipping pod health poll.");
            _connectionState.SetError("aks", "Connection test failed");
            await ClearAllSnapshotsAsync(ct);
            return;
        }

        _connectionState.SetConnected("aks");

        foreach (var ns in namespaces)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            await PollNamespaceAsync(ns, client, ct);
        }
    }

    private async Task PollNamespaceAsync(string ns, IAksClient client, CancellationToken ct)
    {
        IReadOnlyList<PodInfo> pods;
        try
        {
            pods = await client.GetPodsAsync(ns, null, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get pods for namespace {Namespace}", ns);
            return;
        }

        List<PodHealthEvent> eventsToFire;

        await _lock.WaitAsync(ct);
        try
        {
            _namespaceSnapshots.TryGetValue(ns, out var existingSnapshot);

            var now = DateTimeOffset.UtcNow;
            var clusterContext = _appState.Config.AksConfig?.KubeconfigContext ?? "unknown";
            var cooldownMinutes = _appState.Config.AksConfig?.MonitoringCooldownMinutes ?? 10;

            var diffs = PodHealthDiffer.Diff(ns, existingSnapshot, pods, _cooldowns, now);
            eventsToFire = new List<PodHealthEvent>(diffs.Count);

            foreach (var diff in diffs)
            {
                if (_recentEvents.Count >= 100)
                {
                    _recentEvents.RemoveAt(0);
                }

                var evt = new PodHealthEvent(
                    diff.PodName,
                    ns,
                    clusterContext,
                    diff.EventType,
                    diff.PreviousPhase,
                    diff.CurrentPhase,
                    diff.RestartCount,
                    now,
                    diff.Message);

                _recentEvents.Add(evt);
                eventsToFire.Add(evt);

                _cooldowns[PodHealthDiffer.CooldownKey(ns, diff.PodName, diff.EventType)] = now.AddMinutes(cooldownMinutes);
            }

            _namespaceSnapshots[ns] = pods.ToDictionary(
                p => p.Name,
                p => new PodSnapshot(p.Phase, p.ReadyContainers, p.TotalContainers, p.RestartCount));

            var expired = _cooldowns.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList();
            foreach (var key in expired)
            {
                _cooldowns.Remove(key);
            }
        }
        finally
        {
            _lock.Release();
        }

        if (eventsToFire.Count > 0)
        {
            var summary = eventsToFire.Count == 1
                ? $"{eventsToFire[0].PodName}: {eventsToFire[0].CurrentPhase}"
                : $"{eventsToFire.Count} pods affected in {ns}";
            _notifications.ShowWarning(eventsToFire[0].EventType.ToString(), summary);
        }

        foreach (var evt in eventsToFire)
        {
            try
            {
                PodHealthDetected?.Invoke(evt);
                _eventBus.Publish(evt);
                _toastService.ShowPodAlert(evt);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error dispatching pod health event for {PodName}", evt.PodName);
            }
        }
    }

    private async Task TakeBaselineAsync(string ns)
    {
        IAksClient? client;
        await _lock.WaitAsync();
        try
        {
            client = GetOrCreateClient();
        }
        finally
        {
            _lock.Release();
        }

        if (client is null)
        {
            return;
        }

        IReadOnlyList<PodInfo> pods;
        try
        {
            pods = await client.GetPodsAsync(ns, null, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to take baseline snapshot for namespace {Namespace}", ns);
            return;
        }

        await _lock.WaitAsync();
        try
        {
            _namespaceSnapshots[ns] = pods.ToDictionary(
                p => p.Name,
                p => new PodSnapshot(p.Phase, p.ReadyContainers, p.TotalContainers, p.RestartCount));
        }
        finally
        {
            _lock.Release();
        }

        _logger.LogInformation("Baseline snapshot taken for namespace {Namespace} ({PodCount} pods)", ns, pods.Count);
    }

    private async Task ClearAllSnapshotsAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            foreach (var key in _namespaceSnapshots.Keys.ToList())
            {
                _namespaceSnapshots[key] = null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private IAksClient? GetOrCreateClient()
    {
        if (_appState.UseDemoData)
        {
            if (_aksClient is DemoAksClient && _lastWasDemo)
            {
                return _aksClient;
            }

            if (_aksClient is IAsyncDisposable disposable)
            {
                _ = disposable.DisposeAsync();
            }

            _aksClient = new DemoAksClient();
            _lastWasDemo = true;
            _lastKubeconfigContext = null;
            return _aksClient;
        }

        var aksConfig = _appState.Config.AksConfig;
        if (aksConfig is null)
        {
            return null;
        }

        var currentContext = aksConfig.KubeconfigContext;

        if (_aksClient is not null && !_lastWasDemo && currentContext == _lastKubeconfigContext)
        {
            return _aksClient;
        }

        if (_aksClient is IAsyncDisposable asyncDisposable)
        {
            _ = asyncDisposable.DisposeAsync();
        }

        _aksClient = new KubernetesAksClient(aksConfig.KubeconfigContext, aksConfig.KubeconfigPath);
        _lastKubeconfigContext = currentContext;
        _lastWasDemo = false;
        return _aksClient;
    }
}