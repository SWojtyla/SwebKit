using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;

namespace SwebKit.WinUI.ViewModels.Dashboard;

public sealed partial class DashboardPageViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan HealthRefreshBudget = TimeSpan.FromSeconds(8);

    private readonly AppStateService _appState;
    private readonly IConfigurationHealthService _configurationHealth;
    private readonly IConfigurationProbeService _configurationProbes;
    private readonly IDevOpsClientFactory _devOpsClientFactory;
    private readonly IServiceBusClientFactory _serviceBusClientFactory;
    private readonly IAksClientFactory _aksClientFactory;
    private readonly IRedisClientFactory _redisClientFactory;
    private readonly IAppEventBus _events;
    private readonly ICredentialStore _credentialStore;
    private readonly IPodHealthMonitorService _monitor;
    private readonly OperatorWorkspaceService _workspaceService;
    private readonly IShellNavigationService _navigation;
    private readonly DemoDevOpsClient _demoDevOpsClient;
    private readonly DemoAksClient _demoAksClient;
    private readonly DemoRedisClient _demoRedisClient;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILogger<DashboardPageViewModel> _logger;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _pendingRefreshLock = new();
    private readonly HashSet<Task> _pendingRefreshTasks = [];
    private PeriodicTimer? _refreshTimer;
    private Task? _refreshLoopTask;
    private bool _loaded;

    public DashboardPageViewModel(
        AppStateService appState,
        IConfigurationHealthService configurationHealth,
        IConfigurationProbeService configurationProbes,
        IDevOpsClientFactory devOpsClientFactory,
        IServiceBusClientFactory serviceBusClientFactory,
        IAksClientFactory aksClientFactory,
        IRedisClientFactory redisClientFactory,
        IAppEventBus events,
        ICredentialStore credentialStore,
        IPodHealthMonitorService monitor,
        OperatorWorkspaceService workspaceService,
        IShellNavigationService navigation,
        DemoDevOpsClient demoDevOpsClient,
        DemoAksClient demoAksClient,
        DemoRedisClient demoRedisClient,
        ILogger<DashboardPageViewModel> logger)
    {
        _appState = appState;
        _configurationHealth = configurationHealth;
        _configurationProbes = configurationProbes;
        _devOpsClientFactory = devOpsClientFactory;
        _serviceBusClientFactory = serviceBusClientFactory;
        _aksClientFactory = aksClientFactory;
        _redisClientFactory = redisClientFactory;
        _events = events;
        _credentialStore = credentialStore;
        _monitor = monitor;
        _workspaceService = workspaceService;
        _navigation = navigation;
        _demoDevOpsClient = demoDevOpsClient;
        _demoAksClient = demoAksClient;
        _demoRedisClient = demoRedisClient;
        _logger = logger;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("DashboardPageViewModel requires a WinUI dispatcher queue.");

        AttentionAreas.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAttentionAreas));
            OnPropertyChanged(nameof(ReadinessAttentionVisibility));
            OnPropertyChanged(nameof(ShowHealthyReadinessMessage));
        };
        PodHealthAlerts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPodHealthAlerts));
            OnPropertyChanged(nameof(PodHealthSectionVisibility));
            OnPropertyChanged(nameof(ShowNoPodHealthAlerts));
        };
        Activities.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasActivities));
            OnPropertyChanged(nameof(ShowNoActivities));
        };
        FavoriteItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFavoriteItems));
            OnPropertyChanged(nameof(FavoritesBadgeText));
            OnPropertyChanged(nameof(ShowNoFavoriteItems));
        };
        MonitoredNamespaces.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMonitoredNamespaces));
            OnPropertyChanged(nameof(MonitoringBadgeText));
            OnPropertyChanged(nameof(PodHealthSectionVisibility));
        };

        _events.Subscribe<ActivityEvent>(OnActivityReceived);
        _events.Subscribe<RefreshRequestedEvent>(OnRefreshRequested);
        _monitor.PodHealthDetected += OnPodHealthDetected;
        _workspaceService.Changed += OnWorkspaceChanged;
        _appState.DemoModeChanged += OnAppStateSignalsChanged;

        ServiceBusHealth = DashboardHealthTileItem.NotConfigured("Service Bus", "⇄");
        AksHealth = DashboardHealthTileItem.NotConfigured("AKS", "☁");
        RedisHealth = DashboardHealthTileItem.NotConfigured("Redis", "⬡");
        PipelinesHealth = DashboardHealthTileItem.NotConfigured("Pipelines", "🚀");
    }

    public ObservableCollection<DashboardReadinessAreaItem> AttentionAreas { get; } = [];

    public ObservableCollection<DashboardPodHealthAlertItem> PodHealthAlerts { get; } = [];

    public ObservableCollection<DashboardActivityItem> Activities { get; } = [];

    public ObservableCollection<DashboardFavoriteItem> FavoriteItems { get; } = [];

    public ObservableCollection<DashboardNamespaceItem> MonitoredNamespaces { get; } = [];

    [ObservableProperty]
    public partial DashboardHealthTileItem ServiceBusHealth { get; set; }

    [ObservableProperty]
    public partial DashboardHealthTileItem AksHealth { get; set; }

    [ObservableProperty]
    public partial DashboardHealthTileItem RedisHealth { get; set; }

    [ObservableProperty]
    public partial DashboardHealthTileItem PipelinesHealth { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial bool IsDemoMode { get; set; }

    [ObservableProperty]
    public partial bool IsMonitoring { get; set; }

    [ObservableProperty]
    public partial string ReadinessSummary { get; set; } = "Checking configuration readiness...";

    [ObservableProperty]
    public partial string ReadinessLastUpdatedText { get; set; } = "No live readiness check has run yet.";

    [ObservableProperty]
    public partial string HealthyReadinessMessage { get; set; } = "The dashboard is not currently tracking any setup or readiness blockers.";

    public bool HasAttentionAreas => AttentionAreas.Count > 0;

    public bool HasPodHealthAlerts => PodHealthAlerts.Count > 0;

    public bool HasActivities => Activities.Count > 0;

    public bool HasFavoriteItems => FavoriteItems.Count > 0;

    public bool HasMonitoredNamespaces => MonitoredNamespaces.Count > 0;

    public bool ShowNoPodHealthAlerts => !HasPodHealthAlerts;

    public bool ShowNoActivities => !HasActivities;

    public bool ShowNoFavoriteItems => !HasFavoriteItems;

    public bool CanRefresh => !IsRefreshing;

    public string DashboardSubtitle => HasAttentionAreas
        ? "Here is what needs attention before cutting over fully to the WinUI host."
        : "Live readiness, health, pod alerts, recents, and favorites in one native WinUI route.";

    public string FavoritesBadgeText => $"{FavoriteItems.Count} favorite{(FavoriteItems.Count == 1 ? string.Empty : "s")}";

    public string MonitoringBadgeText => !IsMonitoring
        ? "Monitoring off"
        : MonitoredNamespaces.Count == 0
            ? "Monitoring enabled"
            : $"{MonitoredNamespaces.Count} namespace{(MonitoredNamespaces.Count == 1 ? string.Empty : "s")} monitored";

    public string RefreshButtonLabel => IsRefreshing ? "Refreshing..." : "Refresh";

    public Visibility DemoBadgeVisibility => IsDemoMode ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MonitoringBadgeVisibility => IsMonitoring ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ReadinessAttentionVisibility => HasAttentionAreas ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PodHealthSectionVisibility => IsMonitoring || HasPodHealthAlerts ? Visibility.Visible : Visibility.Collapsed;

    public bool ShowHealthyReadinessMessage => !IsRefreshing && !HasAttentionAreas;

    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _appState.WhenInitializedAsync();
        SyncAppSignals();
        SyncFavorites();
        SyncMonitoringState();
        SyncPodHealthAlerts();
        await RunTrackedRefreshAsync(runLiveReadinessProbe: false);
        _refreshLoopTask = Task.Run(() => RefreshLoopAsync(_lifetimeCts.Token));
    }

    [RelayCommand]
    private Task OpenSettingsAsync()
    {
        _navigation.NavigateTo("settings");
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task OpenReadinessAreaAsync(DashboardReadinessAreaItem? item)
    {
        if (item is null)
        {
            return Task.CompletedTask;
        }

        _navigation.NavigateTo(MapDashboardRoute(item.SettingsSection));
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task OpenFavoriteAsync(DashboardFavoriteItem? item) => item is null
        ? Task.CompletedTask
        : _workspaceService.OpenSnapshotAsync(item.Snapshot.Clone(), recordRecent: true);

    [RelayCommand]
    private async Task StopMonitoringNamespaceAsync(DashboardNamespaceItem? item)
    {
        if (item is null)
        {
            return;
        }

        await _monitor.RemoveNamespaceAsync(item.Name);
        SyncMonitoringState();
        SyncPodHealthAlerts();
    }

    [RelayCommand]
    private async Task StopAllMonitoringAsync()
    {
        await _monitor.StopAsync();
        SyncMonitoringState();
        SyncPodHealthAlerts();
    }

    [RelayCommand]
    private Task RefreshDashboardAsync() => RunTrackedRefreshAsync(runLiveReadinessProbe: true);

    public async ValueTask DisposeAsync()
    {
        _events.Unsubscribe<ActivityEvent>(OnActivityReceived);
        _events.Unsubscribe<RefreshRequestedEvent>(OnRefreshRequested);
        _monitor.PodHealthDetected -= OnPodHealthDetected;
        _workspaceService.Changed -= OnWorkspaceChanged;
        _appState.DemoModeChanged -= OnAppStateSignalsChanged;

        _lifetimeCts.Cancel();
        _refreshTimer?.Dispose();

        if (_refreshLoopTask is not null)
        {
            try
            {
                await _refreshLoopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        Task[] pendingRefreshes;
        lock (_pendingRefreshLock)
        {
            pendingRefreshes = _pendingRefreshTasks.ToArray();
        }

        if (pendingRefreshes.Length > 0)
        {
            try
            {
                await Task.WhenAll(pendingRefreshes);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _refreshGate.Dispose();
        _lifetimeCts.Dispose();
    }

    partial void OnIsRefreshingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(RefreshButtonLabel));
        OnPropertyChanged(nameof(ShowHealthyReadinessMessage));
    }

    partial void OnIsDemoModeChanged(bool value)
    {
        OnPropertyChanged(nameof(DemoBadgeVisibility));
    }

    partial void OnIsMonitoringChanged(bool value)
    {
        OnPropertyChanged(nameof(MonitoringBadgeVisibility));
        OnPropertyChanged(nameof(MonitoringBadgeText));
        OnPropertyChanged(nameof(PodHealthSectionVisibility));
    }

    private void OnActivityReceived(ActivityEvent activity)
    {
        ExecuteOnUiThread(() =>
        {
            Activities.Insert(0, new DashboardActivityItem(
                activity.Description,
                string.IsNullOrWhiteSpace(activity.Icon) ? "•" : activity.Icon,
                activity.Area,
                RelativeTime(activity.OccurredAt)));

            while (Activities.Count > 10)
            {
                Activities.RemoveAt(Activities.Count - 1);
            }
        });
    }

    private void OnRefreshRequested(RefreshRequestedEvent refresh)
    {
        if (!string.Equals(refresh.Area, "dashboard", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        QueueRefresh(runLiveReadinessProbe: true);
    }

    private void OnPodHealthDetected(PodHealthEvent evt)
    {
        ExecuteOnUiThread(() =>
        {
            PodHealthAlerts.Insert(0, BuildPodHealthAlertItem(evt));
            while (PodHealthAlerts.Count > 8)
            {
                PodHealthAlerts.RemoveAt(PodHealthAlerts.Count - 1);
            }

            SyncMonitoringState();
        });
    }

    private void OnWorkspaceChanged()
    {
        ExecuteOnUiThread(SyncFavorites);
    }

    private void OnAppStateSignalsChanged()
    {
        QueueRefresh(runLiveReadinessProbe: false);
    }

    private async Task RefreshAsync(bool runLiveReadinessProbe)
    {
        await _appState.WhenInitializedAsync();

        var enteredGate = false;
        try
        {
            enteredGate = await _refreshGate.WaitAsync(0, _lifetimeCts.Token);
            if (!enteredGate)
            {
                return;
            }

            IsRefreshing = true;
            SyncAppSignals();
            SyncFavorites();
            SyncMonitoringState();
            SyncPodHealthAlerts();

            var cachedContext = CreateReadinessContext();
            ApplyReadinessReport(_configurationHealth.BuildReport(cachedContext));

            var serviceBusTask = BuildServiceBusHealthAsync(_lifetimeCts.Token);
            var aksTask = BuildAksHealthAsync(_lifetimeCts.Token);
            var redisTask = BuildRedisHealthAsync(_lifetimeCts.Token);
            var pipelinesTask = BuildPipelinesHealthAsync(_lifetimeCts.Token);

            await Task.WhenAll(serviceBusTask, aksTask, redisTask, pipelinesTask);

            ServiceBusHealth = await serviceBusTask;
            AksHealth = await aksTask;
            RedisHealth = await redisTask;
            PipelinesHealth = await pipelinesTask;

            if (runLiveReadinessProbe)
            {
                var probeContext = cachedContext with { ProbeSnapshot = await _configurationProbes.RunAsync(cachedContext, _lifetimeCts.Token) };
                ApplyReadinessReport(_configurationHealth.BuildReport(probeContext));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WinUI dashboard refresh failed.");
        }
        finally
        {
            IsRefreshing = false;
            if (enteredGate)
            {
                _refreshGate.Release();
            }
        }
    }

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        try
        {
            while (await _refreshTimer.WaitForNextTickAsync(ct))
            {
                QueueRefresh(runLiveReadinessProbe: false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyReadinessReport(ConfigurationHealthReport report)
    {
        var visibleAttentionAreas = report.AttentionAreas
            .Where(area => IsVisibleOnWinUiDashboard(area.AreaKey))
            .ToList();
        var excludedAttentionCount = report.AttentionAreas.Count - visibleAttentionAreas.Count;

        ReadinessSummary = BuildReadinessSummary(report, visibleAttentionAreas.Count, excludedAttentionCount);
        HealthyReadinessMessage = excludedAttentionCount > 0 && visibleAttentionAreas.Count == 0
            ? "Incident Timeline remains intentionally outside the current WinUI cutover plan, so this dashboard only tracks the in-scope WinUI migration areas."
            : "The dashboard is not currently tracking any setup or readiness blockers.";
        ReadinessLastUpdatedText = report.LastProbeCompletedAt is null
            ? "No live readiness check has been recorded yet."
            : $"Live check completed {report.LastProbeCompletedAt.Value.LocalDateTime:g}.";

        ReplaceCollection(
            AttentionAreas,
            visibleAttentionAreas.Select(area => new DashboardReadinessAreaItem(
                area.AreaKey,
                area.SettingsSection,
                area.Title,
                FormatStatus(area.Status),
                area.Summary,
                area.Detail,
                ResolveReadinessActionLabel(area))));

        OnPropertyChanged(nameof(DashboardSubtitle));
    }

    private async Task<DashboardHealthTileItem> BuildServiceBusHealthAsync(CancellationToken ct)
    {
        var configured = _appState.UseDemoData || _appState.ServiceBusNamespaces.Count > 0;
        if (!configured)
        {
            return DashboardHealthTileItem.NotConfigured("Service Bus", "⇄");
        }

        return await BuildHealthTileAsync(
            "Service Bus",
            "⇄",
            async token =>
            {
                long deadLetterCount = 0;

                if (_appState.UseDemoData)
                {
                    deadLetterCount += await SumDeadLetterAsync(DemoServiceBusClient.OrdersDev(), token);
                }
                else
                {
                    foreach (var ns in _appState.ServiceBusNamespaces)
                    {
                        var connectionString = _credentialStore.Get(ns.CredentialKey);
                        if (string.IsNullOrWhiteSpace(connectionString))
                        {
                            continue;
                        }

                        var client = _serviceBusClientFactory.Create(connectionString);
                        try
                        {
                            deadLetterCount += await SumDeadLetterAsync(client, token);
                        }
                        finally
                        {
                            await DisposeServiceBusClientAsync(client);
                        }
                    }
                }

                return new DashboardHealthMetric((int)deadLetterCount, "dead-lettered", DateTimeOffset.Now);
            },
            ct);
    }

    private async Task<DashboardHealthTileItem> BuildAksHealthAsync(CancellationToken ct)
    {
        var configured = _appState.UseDemoData || _appState.Config.AksConfig is not null;
        if (!configured)
        {
            return DashboardHealthTileItem.NotConfigured("AKS", "☁");
        }

        return await BuildHealthTileAsync(
            "AKS",
            "☁",
            async token =>
            {
                var aksConfig = _appState.Config.AksConfig;
                var ns = aksConfig?.DefaultNamespace ?? "default";

                var client = _appState.UseDemoData
                    ? _demoAksClient
                    : _aksClientFactory.Create(aksConfig!.KubeconfigContext, aksConfig.KubeconfigPath);

                var pods = await client.GetPodsAsync(ns, null, token);
                var unhealthyPods = pods.Count(pod => pod.Status is not ("Running" or "Succeeded" or "Completed"));
                return new DashboardHealthMetric(unhealthyPods, "unhealthy pods", DateTimeOffset.Now);
            },
            ct);
    }

    private async Task<DashboardHealthTileItem> BuildRedisHealthAsync(CancellationToken ct)
    {
        var configured = _appState.UseDemoData || _appState.Config.RedisConfig?.ActiveCache is not null;
        if (!configured)
        {
            return DashboardHealthTileItem.NotConfigured("Redis", "⬡");
        }

        return await BuildHealthTileAsync(
            "Redis",
            "⬡",
            async token =>
            {
                using var client = _appState.UseDemoData
                    ? _demoRedisClient
                    : await _redisClientFactory.CreateAsync(_appState.Config.RedisConfig!.ActiveCache!, token);

                var scan = await client.ScanKeysAsync("*", 0, 100, token);
                var infoTasks = scan.Keys.Select(key => client.GetKeyInfoAsync(key, token));
                var infos = await Task.WhenAll(infoTasks);
                var nearExpiry = infos.Count(info => info.Ttl is { } ttl && ttl < TimeSpan.FromMinutes(5) && ttl > TimeSpan.Zero);
                return new DashboardHealthMetric(nearExpiry, "keys expiring < 5m", DateTimeOffset.Now);
            },
            ct);
    }

    private async Task<DashboardHealthTileItem> BuildPipelinesHealthAsync(CancellationToken ct)
    {
        var configured = _appState.UseDemoData
            || (_appState.Config.DevOpsConfig is not null && !string.IsNullOrWhiteSpace(_appState.Config.DevOpsConfig.Organization));
        if (!configured)
        {
            return DashboardHealthTileItem.NotConfigured("Pipelines", "🚀");
        }

        return await BuildHealthTileAsync(
            "Pipelines",
            "🚀",
            async token =>
            {
                var client = _appState.UseDemoData
                    ? _demoDevOpsClient
                    : _devOpsClientFactory.Create(_appState.Config.DevOpsConfig!);

                var projects = await client.GetProjectsAsync(token);
                var approvalTasks = projects.Select(project => client.GetPendingApprovalsAsync(project.Name, token));
                var results = await Task.WhenAll(approvalTasks);
                var total = results.Sum(result => result.Count);
                return new DashboardHealthMetric(total, "pending approvals", DateTimeOffset.Now);
            },
            ct);
    }

    private async Task<DashboardHealthTileItem> BuildHealthTileAsync(
        string title,
        string glyph,
        Func<CancellationToken, Task<DashboardHealthMetric>> fetchMetric,
        CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(HealthRefreshBudget);

        try
        {
            var metric = await fetchMetric(timeoutCts.Token);
            return DashboardHealthTileItem.Ready(title, glyph, metric, $"Last updated {metric.LastUpdated.LocalDateTime:g}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return DashboardHealthTileItem.Warning(title, glyph, $"Timed out while refreshing {title}.");
        }
        catch (Exception ex)
        {
            return DashboardHealthTileItem.Warning(title, glyph, ex.Message);
        }
    }

    private ConfigurationHealthContext CreateReadinessContext()
    {
        var baseContext = new ConfigurationHealthContext(
            _appState.Config,
            _appState.ServiceBusNamespaces,
            _appState.UseDemoData,
            _appState.HasProfileLoadFailure,
            _appState.ProfilePersistenceBlockedMessage);

        return baseContext with { ProbeSnapshot = _configurationProbes.GetLatest(baseContext) };
    }

    private void SyncAppSignals()
    {
        IsDemoMode = _appState.UseDemoData;
    }

    private void SyncMonitoringState()
    {
        IsMonitoring = _monitor.IsMonitoring;
        ReplaceCollection(MonitoredNamespaces, _monitor.MonitoredNamespaces.Select(static ns => new DashboardNamespaceItem(ns)));
    }

    private void SyncPodHealthAlerts()
    {
        ReplaceCollection(
            PodHealthAlerts,
            _monitor.RecentEvents
                .OrderByDescending(static evt => evt.DetectedAt)
                .Take(8)
                .Select(BuildPodHealthAlertItem));
    }

    private void SyncFavorites()
    {
        ReplaceCollection(
            FavoriteItems,
            _workspaceService.GetFavoriteResources()
                .Take(8)
                .Select(BuildFavoriteItem));
    }

    private static DashboardPodHealthAlertItem BuildPodHealthAlertItem(PodHealthEvent evt) =>
        new(
            evt.PodName,
            evt.Namespace,
            evt.EventType.ToString(),
            string.IsNullOrWhiteSpace(evt.CurrentPhase)
                ? evt.EventType.ToString()
                : evt.CurrentPhase,
            RelativeTime(evt.DetectedAt));

    private static DashboardFavoriteItem BuildFavoriteItem(FavoriteResource favorite)
    {
        var icon = favorite.Snapshot.Resource.Area switch
        {
            "service-bus" => "⇄",
            "aks" => "☁",
            "observability" => "📈",
            "storage" => "📁",
            "redis" => "⚡",
            _ => favorite.Snapshot.Resource.Icon ?? "⌘"
        };

        var displayPath = string.IsNullOrWhiteSpace(favorite.Name)
            ? favorite.Snapshot.Resource.DisplayPath ?? favorite.Snapshot.Resource.DisplayName
            : favorite.Name.Trim();

        var summary = string.IsNullOrWhiteSpace(favorite.Snapshot.Resource.Summary)
            ? favorite.Snapshot.Resource.DisplayName
            : favorite.Snapshot.Resource.Summary!;

        return new DashboardFavoriteItem(displayPath, icon, summary, favorite.Snapshot.Clone());
    }

    private static string FormatStatus(ConfigurationCheckStatus status) => status switch
    {
        ConfigurationCheckStatus.Ready => "Ready",
        ConfigurationCheckStatus.Configured => "Configured",
        ConfigurationCheckStatus.Warning => "Needs attention",
        ConfigurationCheckStatus.Error => "Error",
        ConfigurationCheckStatus.NotConfigured => "Needs setup",
        ConfigurationCheckStatus.Skipped => "Skipped",
        _ => status.ToString()
    };

    private Task RunTrackedRefreshAsync(bool runLiveReadinessProbe)
    {
        var refreshTask = RefreshAsync(runLiveReadinessProbe);
        TrackRefreshTask(refreshTask);
        return refreshTask;
    }

    private void QueueRefresh(bool runLiveReadinessProbe)
    {
        if (_lifetimeCts.IsCancellationRequested)
        {
            return;
        }

        ExecuteOnUiThread(() =>
        {
            if (_lifetimeCts.IsCancellationRequested)
            {
                return;
            }

            _ = RunTrackedRefreshAsync(runLiveReadinessProbe);
        });
    }

    private void TrackRefreshTask(Task refreshTask)
    {
        lock (_pendingRefreshLock)
        {
            _pendingRefreshTasks.Add(refreshTask);
        }

        _ = refreshTask.ContinueWith(
            _ =>
            {
                lock (_pendingRefreshLock)
                {
                    _pendingRefreshTasks.Remove(refreshTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static string MapDashboardRoute(string settingsSection) => settingsSection switch
    {
        "servicebus" => "service-bus",
        "aks" => "aks",
        "redis" => "redis",
        "devops" => "pipelines",
        "storage" => "storage",
        "observability" => "observability",
        _ => "settings"
    };

    private static string ResolveReadinessActionLabel(ConfigurationAreaHealth area) => MapDashboardRoute(area.SettingsSection) switch
    {
        "service-bus" => "Open Service Bus workspace",
        "aks" => "Open AKS workspace",
        "redis" => "Open Redis workspace",
        "storage" => "Open Storage workspace",
        "pipelines" => "Open Pipelines workspace",
        "observability" => "Open Observability workspace",
        _ => "Open Settings"
    };

    private static string BuildReadinessSummary(
        ConfigurationHealthReport report,
        int visibleAttentionAreaCount,
        int excludedAttentionCount)
    {
        if (excludedAttentionCount == 0)
        {
            return report.Summary;
        }

        if (visibleAttentionAreaCount == 0)
        {
            return "Core WinUI cutover areas are configured. Incident Timeline remains outside the current WinUI migration scope.";
        }

        var areaLabel = visibleAttentionAreaCount == 1 ? "area" : "areas";
        return $"{visibleAttentionAreaCount} WinUI cutover {areaLabel} still need attention. Incident Timeline remains outside the current WinUI migration scope.";
    }

    private static bool IsVisibleOnWinUiDashboard(string areaKey) =>
        !string.Equals(areaKey, "incident-timeline", StringComparison.OrdinalIgnoreCase);

    private static async Task<long> SumDeadLetterAsync(IServiceBusClient client, CancellationToken ct)
    {
        var queues = await client.ListQueuesAsync(ct);
        return queues.Sum(queue => queue.Stats?.DeadLetterMessageCount ?? 0);
    }

    private static async Task DisposeServiceBusClientAsync(IServiceBusClient client)
    {
        switch (client)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }

    private static string RelativeTime(DateTimeOffset at)
    {
        var elapsed = DateTimeOffset.Now - at;
        if (elapsed.TotalSeconds < 60)
        {
            return $"{Math.Max(1, (int)elapsed.TotalSeconds)}s ago";
        }

        if (elapsed.TotalMinutes < 60)
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }

        return $"{(int)elapsed.TotalHours}h ago";
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void ExecuteOnUiThread(Action action)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() => action());
    }
}