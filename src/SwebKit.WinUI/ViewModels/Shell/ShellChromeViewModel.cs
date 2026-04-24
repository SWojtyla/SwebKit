using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;

namespace SwebKit.WinUI.ViewModels.Shell;

public sealed partial class ShellChromeViewModel : ObservableObject
{
    private static readonly ShellAreaDescriptor DefaultArea = new(
        "shell",
        "Overview",
        "Workspace shell",
        "Select a workspace from the left navigation to open resources, recents, and diagnostics.");

    private static readonly IReadOnlyDictionary<string, ShellAreaDescriptor> AreaDescriptors =
        new Dictionary<string, ShellAreaDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["service-bus"] = new("service-bus", "Workspaces", "Service Bus", "Browse queues, dead letters, and scheduled messages."),
            ["aks"] = new("aks", "Workspaces", "AKS", "Inspect clusters, workloads, and live pod operations."),
            ["redis"] = new("redis", "Workspaces", "Redis", "Explore keyspaces, health, and value operations."),
            ["storage"] = new("storage", "Workspaces", "Storage", "Inspect blob containers, objects, and versions."),
            ["pipelines"] = new("pipelines", "Delivery", "Pipelines", "Track delivery activity, releases, and approvals."),
            ["observability"] = new("observability", "Signals", "Observability", "Query Application Insights health, failures, and logs."),
            ["incident-timeline"] = new("incident-timeline", "Signals", "Incident Timeline", "Correlate deployment, runtime, and messaging evidence."),
            ["settings"] = new("settings", "Configuration", "Settings", "Manage configuration, theme, and safety defaults."),
        };

    private readonly MainWindowViewModel _navigation;
    private readonly AppStateService _appState;
    private readonly IConnectionStateService _connectionState;
    private readonly OperatorWorkspaceService _workspaceService;
    private readonly INotificationService _notificationService;
    private readonly UiStateRepository _uiState;
    private readonly DispatcherQueue _dispatcherQueue;
    private WorkspaceSnapshot? _currentSnapshot;
    private int _lastSeenNotificationCount;
    private bool _hasInitializedUiState;

    public ShellChromeViewModel(
        MainWindowViewModel navigation,
        AppStateService appState,
        IConnectionStateService connectionState,
        OperatorWorkspaceService workspaceService,
        INotificationService notificationService,
        UiStateRepository uiState)
    {
        _navigation = navigation;
        _appState = appState;
        _connectionState = connectionState;
        _workspaceService = workspaceService;
        _notificationService = notificationService;
        _uiState = uiState;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("ShellChromeViewModel requires a WinUI dispatcher queue.");

        FavoriteResources = [];
        RecentResources = [];
        NotificationHistory = [];

        _lastSeenNotificationCount = _uiState.State.NotificationHistory.Count;

        _navigation.NavigationChanged += OnShellStateChanged;
        _connectionState.StatesChanged += OnShellStateChanged;
        _workspaceService.Changed += OnWorkspaceChanged;
        _notificationService.NotificationsChanged += OnNotificationsChanged;
        _appState.Initialized += OnAppStateInitialized;
        _appState.DemoModeChanged += OnAppStateSignalsChanged;

        RefreshAll();
    }

    public ObservableCollection<FavoriteResourceItem> FavoriteResources { get; }

    public ObservableCollection<RecentResourceItem> RecentResources { get; }

    public ObservableCollection<NotificationHistoryItem> NotificationHistory { get; }

    [ObservableProperty]
    public partial string CurrentAreaGroupLabel { get; set; } = DefaultArea.GroupLabel;

    [ObservableProperty]
    public partial string CurrentAreaTitle { get; set; } = DefaultArea.Title;

    [ObservableProperty]
    public partial string CurrentAreaSummary { get; set; } = DefaultArea.Summary;

    [ObservableProperty]
    public partial string CurrentConnectionLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CurrentConnectionDetail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasCurrentSnapshot { get; set; }

    [ObservableProperty]
    public partial string CurrentResourceTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CurrentResourceSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FavoriteName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCurrentFavorite { get; set; }

    [ObservableProperty]
    public partial int UnreadNotificationCount { get; set; }

    [ObservableProperty]
    public partial bool IsProduction { get; set; }

    [ObservableProperty]
    public partial bool IsDemoMode { get; set; }

    [ObservableProperty]
    public partial string ProfileFailureMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProfileRecoveryMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowProfileFailureBanner { get; set; }

    [ObservableProperty]
    public partial bool ShowProfileRecoveryBanner { get; set; }

    public bool ShowProductionBanner => IsProduction;

    public bool ShowDemoBanner => IsDemoMode;

    public bool ShowNoCurrentSnapshot => !HasCurrentSnapshot;

    public bool ShowNoFavorites => FavoriteResources.Count == 0;

    public bool ShowNoRecentResources => RecentResources.Count == 0;

    public bool ShowNoNotifications => NotificationHistory.Count == 0;

    public Visibility CurrentSnapshotVisibility => HasCurrentSnapshot ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CurrentFavoriteVisibility => IsCurrentFavorite ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FavoriteListVisibility => ShowNoFavorites ? Visibility.Collapsed : Visibility.Visible;

    public Visibility RecentListVisibility => ShowNoRecentResources ? Visibility.Collapsed : Visibility.Visible;

    public Visibility NotificationListVisibility => ShowNoNotifications ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CurrentConnectionVisibility => string.IsNullOrWhiteSpace(_navigation.CurrentArea)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility ProductionBadgeVisibility => IsProduction ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DemoBadgeVisibility => IsDemoMode ? Visibility.Visible : Visibility.Collapsed;

    public string SaveFavoriteButtonLabel => IsCurrentFavorite ? "Update favorite" : "Save favorite";

    public string WorkspaceButtonLabel => FavoriteResources.Count > 0
        ? $"Workspace hub ({FavoriteResources.Count})"
        : "Workspace hub";

    public string NotificationButtonLabel => UnreadNotificationCount > 0
        ? $"Notifications ({UnreadNotificationCount})"
        : "Notifications";

    public string StatusPrimaryText => string.IsNullOrWhiteSpace(_navigation.CurrentArea)
        ? "Shell ready"
        : $"{CurrentAreaGroupLabel}: {CurrentAreaTitle}";

    public string StatusSecondaryText => string.IsNullOrWhiteSpace(CurrentConnectionDetail)
        ? CurrentConnectionLabel
        : $"{CurrentConnectionLabel} · {CurrentConnectionDetail}";

    public string StatusTertiaryText => $"{FavoriteResources.Count} favorites · {RecentResources.Count} recent · {NotificationHistory.Count} notifications";

    public void MarkNotificationsSeen()
    {
        ExecuteOnUiThread(() =>
        {
            _lastSeenNotificationCount = _uiState.State.NotificationHistory.Count;
            RefreshNotificationState();
        });
    }

    partial void OnHasCurrentSnapshotChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNoCurrentSnapshot));
        OnPropertyChanged(nameof(CurrentSnapshotVisibility));
    }

    partial void OnIsCurrentFavoriteChanged(bool value)
    {
        OnPropertyChanged(nameof(CurrentFavoriteVisibility));
        OnPropertyChanged(nameof(SaveFavoriteButtonLabel));
    }

    partial void OnIsProductionChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowProductionBanner));
        OnPropertyChanged(nameof(ProductionBadgeVisibility));
    }

    partial void OnIsDemoModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDemoBanner));
        OnPropertyChanged(nameof(DemoBadgeVisibility));
    }

    [RelayCommand]
    private async Task SaveCurrentFavoriteAsync()
    {
        if (_currentSnapshot is null)
        {
            return;
        }

        await _workspaceService.SaveFavoriteAsync(_currentSnapshot, FavoriteName);
        RefreshWorkspaceState();
        RefreshConnectionState();
        RaiseShellComputedProperties();
    }

    [RelayCommand]
    private async Task RemoveCurrentFavoriteAsync()
    {
        if (_currentSnapshot is null)
        {
            return;
        }

        await _workspaceService.RemoveFavoriteAsync(_currentSnapshot.Resource.Key);
        RefreshWorkspaceState();
        RefreshConnectionState();
        RaiseShellComputedProperties();
    }

    [RelayCommand]
    private Task OpenCurrentSnapshotAsync() => _currentSnapshot is null
        ? Task.CompletedTask
        : _workspaceService.OpenSnapshotAsync(_currentSnapshot, recordRecent: true);

    [RelayCommand]
    private Task OpenFavoriteAsync(FavoriteResourceItem? item) => item is null
        ? Task.CompletedTask
        : _workspaceService.OpenFavoriteAsync(item.ResourceKey);

    [RelayCommand]
    private async Task RemoveFavoriteAsync(FavoriteResourceItem? item)
    {
        if (item is null)
        {
            return;
        }

        await _workspaceService.RemoveFavoriteAsync(item.ResourceKey);
        RefreshWorkspaceState();
        RefreshConnectionState();
        RaiseShellComputedProperties();
    }

    [RelayCommand]
    private Task OpenRecentAsync(RecentResourceItem? item) => item is null
        ? Task.CompletedTask
        : _workspaceService.OpenSnapshotAsync(item.Snapshot.Clone(), recordRecent: true);

    [RelayCommand]
    private void ClearNotifications()
    {
        _notificationService.ClearAll();
    }

    private void OnShellStateChanged()
    {
        ExecuteOnUiThread(() =>
        {
            RefreshAreaContext();
            RefreshWorkspaceState();
            RefreshConnectionState();
            RaiseShellComputedProperties();
        });
    }

    private void OnWorkspaceChanged()
    {
        ExecuteOnUiThread(() =>
        {
            RefreshWorkspaceState();
            RefreshConnectionState();
            RaiseShellComputedProperties();
        });
    }

    private void OnNotificationsChanged()
    {
        ExecuteOnUiThread(RefreshNotificationState);
    }

    private void OnAppStateInitialized()
    {
        ExecuteOnUiThread(() =>
        {
            if (!_hasInitializedUiState)
            {
                _lastSeenNotificationCount = _uiState.State.NotificationHistory.Count;
                _hasInitializedUiState = true;
            }

            RefreshAll();
        });
    }

    private void OnAppStateSignalsChanged()
    {
        ExecuteOnUiThread(() =>
        {
            RefreshAppState();
            RaiseShellComputedProperties();
        });
    }

    private void RefreshAll()
    {
        RefreshAreaContext();
        RefreshWorkspaceState();
        RefreshConnectionState();
        RefreshNotificationState();
        RefreshAppState();
        RaiseShellComputedProperties();
    }

    private void RefreshAreaContext()
    {
        var descriptor = string.IsNullOrWhiteSpace(_navigation.CurrentArea)
            ? DefaultArea
            : AreaDescriptors.TryGetValue(_navigation.CurrentArea, out var resolved)
                ? resolved
                : DefaultArea;

        CurrentAreaGroupLabel = descriptor.GroupLabel;
        CurrentAreaTitle = descriptor.Title;
        CurrentAreaSummary = descriptor.Summary;

        OnPropertyChanged(nameof(CurrentConnectionVisibility));
        OnPropertyChanged(nameof(StatusPrimaryText));
    }

    private void RefreshConnectionState()
    {
        if (!string.IsNullOrWhiteSpace(_navigation.CurrentArea)
            && _connectionState.States.TryGetValue(_navigation.CurrentArea, out var state))
        {
            CurrentConnectionLabel = state.State switch
            {
                ConnectionState.Connected => "Connected",
                ConnectionState.Error => "Needs attention",
                ConnectionState.NotConfigured => "Needs setup",
                _ => "Waiting for telemetry",
            };

            CurrentConnectionDetail = state.State switch
            {
                ConnectionState.Connected when !string.IsNullOrWhiteSpace(CurrentResourceTitle) => CurrentResourceTitle,
                ConnectionState.Connected => "Workspace connected.",
                ConnectionState.Error => string.IsNullOrWhiteSpace(state.ErrorMessage) ? "Recent connection check failed." : state.ErrorMessage,
                ConnectionState.NotConfigured => "Configure this workspace before opening live resources.",
                _ => "Open a workspace to surface live status.",
            };
        }
        else if (string.IsNullOrWhiteSpace(_navigation.CurrentArea))
        {
            CurrentConnectionLabel = "Select a workspace";
            CurrentConnectionDetail = "Connection and resource state will appear after opening a route.";
        }
        else
        {
            CurrentConnectionLabel = "Waiting for telemetry";
            CurrentConnectionDetail = "Open or connect a resource to surface live state.";
        }

        OnPropertyChanged(nameof(StatusSecondaryText));
    }

    private void RefreshWorkspaceState()
    {
        _currentSnapshot = string.IsNullOrWhiteSpace(_navigation.CurrentArea)
            ? null
            : _workspaceService.GetCurrentSnapshot(_navigation.CurrentArea!);

        HasCurrentSnapshot = _currentSnapshot is not null;

        if (_currentSnapshot is null)
        {
            CurrentResourceTitle = string.Empty;
            CurrentResourceSummary = string.Empty;
            FavoriteName = string.Empty;
            IsCurrentFavorite = false;
        }
        else
        {
            CurrentResourceTitle = _currentSnapshot.Resource.DisplayPath ?? _currentSnapshot.Resource.DisplayName;
            CurrentResourceSummary = BuildResourceSummary(_currentSnapshot);

            var favorite = _workspaceService.GetFavoriteResource(_currentSnapshot.Resource.Key);
            IsCurrentFavorite = favorite is not null;
            FavoriteName = favorite?.Name ?? CurrentResourceTitle;
        }

        ReplaceCollection(
            FavoriteResources,
            _workspaceService.GetFavoriteResources()
                .Take(5)
                .Select(static favorite => new FavoriteResourceItem(
                    favorite.Name,
                    favorite.Snapshot.Resource.DisplayPath ?? favorite.Snapshot.Resource.DisplayName,
                    BuildResourceSummary(favorite.Snapshot),
                    favorite.Snapshot.Resource.Key)));

        ReplaceCollection(
            RecentResources,
            _workspaceService.GetRecentResources()
                .Take(5)
                .Select(static recent => new RecentResourceItem(
                    recent.Snapshot.Resource.DisplayPath ?? recent.Snapshot.Resource.DisplayName,
                    BuildResourceSummary(recent.Snapshot),
                    recent.AccessedAt.ToLocalTime().ToString("g"),
                    recent.Snapshot.Clone())));

        OnPropertyChanged(nameof(ShowNoFavorites));
        OnPropertyChanged(nameof(ShowNoRecentResources));
        OnPropertyChanged(nameof(FavoriteListVisibility));
        OnPropertyChanged(nameof(RecentListVisibility));
        OnPropertyChanged(nameof(WorkspaceButtonLabel));
        OnPropertyChanged(nameof(StatusTertiaryText));
    }

    private void RefreshNotificationState()
    {
        var history = _uiState.State.NotificationHistory
            .OrderByDescending(static notification => notification.Timestamp)
            .Take(12)
            .Select(static notification => new NotificationHistoryItem(
                notification.Severity,
                notification.Message,
                notification.Detail,
                notification.Timestamp.ToLocalTime().ToString("g")))
            .ToList();

        ReplaceCollection(NotificationHistory, history);

        if (_lastSeenNotificationCount > _uiState.State.NotificationHistory.Count)
        {
            _lastSeenNotificationCount = _uiState.State.NotificationHistory.Count;
        }

        UnreadNotificationCount = Math.Max(0, _uiState.State.NotificationHistory.Count - _lastSeenNotificationCount);

        OnPropertyChanged(nameof(ShowNoNotifications));
        OnPropertyChanged(nameof(NotificationListVisibility));
        OnPropertyChanged(nameof(NotificationButtonLabel));
        OnPropertyChanged(nameof(StatusTertiaryText));
    }

    private void RefreshAppState()
    {
        IsProduction = _appState.Config.IsProduction;
        IsDemoMode = _appState.UseDemoData;

        ShowProfileFailureBanner = _appState.HasProfileLoadFailure || _appState.IsProfilePersistenceBlocked;
        ShowProfileRecoveryBanner = _appState.HasProfileLoadRecovery && !ShowProfileFailureBanner;

        ProfileFailureMessage = _appState.ProfilePersistenceBlockedMessage
            ?? _appState.ProfileLoadResult.ErrorMessage
            ?? "The main profile file could not be loaded. Saving stays blocked to avoid overwriting the existing file.";

        var recoverySource = _appState.ProfileLoadResult.RecoverySourcePath;
        ProfileRecoveryMessage = string.IsNullOrWhiteSpace(recoverySource)
            ? "SwebKit restored profile data from a backup copy after the primary file could not be read."
            : $"SwebKit restored profile data from '{Path.GetFileName(recoverySource)}' after the primary file could not be read.";
    }

    private void RaiseShellComputedProperties()
    {
        OnPropertyChanged(nameof(StatusPrimaryText));
        OnPropertyChanged(nameof(StatusSecondaryText));
        OnPropertyChanged(nameof(StatusTertiaryText));
        OnPropertyChanged(nameof(WorkspaceButtonLabel));
        OnPropertyChanged(nameof(NotificationButtonLabel));
    }

    private static string BuildResourceSummary(WorkspaceSnapshot snapshot)
    {
        var summaryParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(snapshot.Resource.DisplayName)
            && !string.Equals(snapshot.Resource.DisplayName, snapshot.Resource.DisplayPath, StringComparison.OrdinalIgnoreCase))
        {
            summaryParts.Add(snapshot.Resource.DisplayName);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Resource.Summary))
        {
            summaryParts.Add(snapshot.Resource.Summary);
        }
        else if (!string.IsNullOrWhiteSpace(snapshot.Resource.Kind))
        {
            summaryParts.Add(snapshot.Resource.Kind);
        }

        if (summaryParts.Count == 0)
        {
            summaryParts.Add($"Captured {snapshot.CapturedAt.ToLocalTime():g}");
        }

        return string.Join(" · ", summaryParts);
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

    private sealed record ShellAreaDescriptor(string Area, string GroupLabel, string Title, string Summary);
}

public sealed record FavoriteResourceItem(string Name, string Title, string Summary, string ResourceKey);

public sealed record RecentResourceItem(string Title, string Summary, string TimestampText, WorkspaceSnapshot Snapshot);

public sealed record NotificationHistoryItem(string Severity, string Message, string? Detail, string TimestampText)
{
    public Visibility DetailVisibility => string.IsNullOrWhiteSpace(Detail) ? Visibility.Collapsed : Visibility.Visible;
}