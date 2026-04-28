using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;
using SwebKit.WinUI.ViewModels.Settings;

namespace SwebKit.WinUI.ViewModels.Aks;

public sealed partial class AksPageViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AppStateService _appState;
    private readonly IAksClientBootstrapper _bootstrapper;
    private readonly IShellNavigationService _navigation;
    private readonly IPodHealthMonitorService _monitor;
    private readonly IPortForwardSessionService _portForwardSessions;
    private readonly INotificationService _notifications;
    private readonly ILogger<AksPageViewModel> _logger;
    private CancellationTokenSource _loadCts = new();
    private CancellationTokenSource _selectedResourceActionCts = new();
    private bool _isDisposed;
    private bool _loaded;
    private bool _hasSeededSelectionFromConfig;
    private bool _suppressSelectionSideEffects;

    public AksPageViewModel(
        AppStateService appState,
        IAksClientBootstrapper bootstrapper,
        IShellNavigationService navigation,
        IPodHealthMonitorService monitor,
        IPortForwardSessionService portForwardSessions,
        INotificationService notifications,
        ILogger<AksPageViewModel> logger)
    {
        _appState = appState;
        _bootstrapper = bootstrapper;
        _navigation = navigation;
        _monitor = monitor;
        _portForwardSessions = portForwardSessions;
        _notifications = notifications;
        _logger = logger;

        ContextOptions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasContextOptions));
        NamespaceOptions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNamespaceOptions));
        Pods.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(PodCountLabel));
            OnPropertyChanged(nameof(ShowPodEmptyState));
        };
        Events.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasEvents));
            OnPropertyChanged(nameof(WarningEventCount));
            OnPropertyChanged(nameof(EventsSummary));
            OnPropertyChanged(nameof(ToggleEventsButtonText));
            OnPropertyChanged(nameof(ShowEventsEmptyState));
        };

        InitializeMonitoringState();
        InitializePortForwardState();
    }

    public ObservableCollection<string> ContextOptions { get; } = [];

    public ObservableCollection<string> NamespaceOptions { get; } = [];

    public ObservableCollection<AksPodItemViewModel> Pods { get; } = [];

    public ObservableCollection<AksClusterEventItemViewModel> Events { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string ConnectionSummary { get; set; } = "AKS not connected.";

    [ObservableProperty]
    public partial string SelectedContext { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedNamespace { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IAksClient? Client { get; set; }

    [ObservableProperty]
    public partial bool ShowEvents { get; set; }

    [ObservableProperty]
    public partial string? EventsErrorMessage { get; set; }

    public bool IsConfigured => _appState.UseDemoData || _appState.Config.AksConfig is not null;

    public bool IsConnected => Client is not null;

    public bool HasContextOptions => ContextOptions.Count > 0;

    public bool HasNamespaceOptions => NamespaceOptions.Count > 0;

    public bool HasEvents => Events.Count > 0;

    public int WarningEventCount => Events.Count(item => item.IsWarning);

    public bool ShowNotConfiguredState => !IsLoading && !IsConfigured;

    public bool ShowPodEmptyState => IsConnected && !IsLoading && Pods.Count == 0 && ErrorMessage is null;

    public string PodCountLabel => Pods.Count == 1 ? "1 pod" : $"{Pods.Count} pods";

    public string EventsSummary => Events.Count switch
    {
        0 when string.IsNullOrWhiteSpace(SelectedNamespace) => "No namespace selected for event inspection.",
        0 when string.Equals(SelectedNamespace, "*", StringComparison.Ordinal) => "Recent events load only after selecting a specific namespace.",
        0 => "No recent events were surfaced for the current namespace.",
        1 => WarningEventCount == 1 ? "1 recent event · 1 warning" : "1 recent event",
        _ => WarningEventCount == 0 ? $"{Events.Count:N0} recent events" : $"{Events.Count:N0} recent events · {WarningEventCount:N0} warning",
    };

    public string ToggleEventsButtonText => ShowEvents
        ? WarningEventCount == 0 ? "Hide events" : $"Hide events ({WarningEventCount})"
        : WarningEventCount == 0 ? "Show events" : $"Show events ({WarningEventCount})";

    public Microsoft.UI.Xaml.Visibility EventsSectionVisibility => ShowEvents
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility EventsErrorVisibility => string.IsNullOrWhiteSpace(EventsErrorMessage)
        ? Microsoft.UI.Xaml.Visibility.Collapsed
        : Microsoft.UI.Xaml.Visibility.Visible;

    public bool ShowEventsEmptyState => ShowEvents && !IsLoading && string.IsNullOrWhiteSpace(EventsErrorMessage) && Events.Count == 0;

    public async Task LoadAsync()
    {
        if (_loaded || _isDisposed)
        {
            return;
        }

        _loaded = true;

        if (_isDisposed)
        {
            return;
        }

        await ReloadAsync();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await _appState.WhenInitializedAsync();

        if (_isDisposed)
        {
            return;
        }

        SyncMonitoringState();
        SyncPodHealthAlerts();

        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(ShowNotConfiguredState));

        SeedSelectionFromConfig();

        if (!IsConfigured)
        {
            Client = null;
            ConnectionSummary = "No AKS configuration found. Configure kubeconfig settings in Settings before opening this workspace.";
            ErrorMessage = null;
            EventsErrorMessage = null;
            Events.Clear();
            ContextOptions.Clear();
            NamespaceOptions.Clear();
            Pods.Clear();
            ClearMonitorNamespaceOptions();
            ClearResourceExplorerState();
            SelectedPod = null;
            SelectedContext = string.Empty;
            SelectedNamespace = string.Empty;
            OnPropertyChanged(nameof(IsConnected));
            return;
        }

        await SuspendSelectedPodLogsForReloadAsync("Reloading AKS scope...");
        await SuspendSelectedWorkloadLogsForReloadAsync("Reloading AKS scope...");
        Pods.Clear();
        OnPropertyChanged(nameof(PodCountLabel));
        OnPropertyChanged(nameof(ShowPodEmptyState));
        await ResetLoadTokenAsync();
        IsLoading = true;
        ErrorMessage = null;
        await Task.Yield();

        if (_isDisposed)
        {
            IsLoading = false;
            return;
        }

        try
        {
            var result = await _bootstrapper.BootstrapAsync(
                new AksClientBootstrapRequest(
                    ClientOverride: null,
                    UseDemoData: _appState.UseDemoData,
                    Config: _appState.Config.AksConfig,
                    RequestedContext: string.IsNullOrWhiteSpace(SelectedContext) ? null : SelectedContext,
                    RequestedNamespace: string.IsNullOrWhiteSpace(SelectedNamespace) ? null : SelectedNamespace),
                _loadCts.Token);

            ApplyBootstrapResult(result);

            if (result.Status == AksClientBootstrapStatus.Connected && result.Client is not null)
            {
                await LoadPodsAsync(_loadCts.Token);
            }
            else if (result.Status == AksClientBootstrapStatus.Error)
            {
                ClearResourceExplorerState();
                SelectedPod = null;
                ErrorMessage = result.ErrorMessage;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ClearResourceExplorerState();
            SelectedPod = null;
            ErrorMessage = ex.Message;
            ConnectionSummary = "AKS bootstrap failed.";
            _logger.LogError(ex, "WinUI AKS page reload failed.");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowPodEmptyState));
        }
    }

    [RelayCommand]
    private Task OpenSettingsAsync()
    {
        _navigation.NavigateTo("settings", new SettingsNavigationRequest(SettingsSections.Aks));
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        DisposeMonitoringState();
        DisposePortForwardState();
        ResetSelectedResourceBusyStateForDispose();

        if (!_selectedResourceActionCts.IsCancellationRequested)
        {
            await _selectedResourceActionCts.CancelAsync();
        }

        await ResetLoadTokenAsync();
        await ResetLogsTokenAsync();
        await ResetWorkloadLogsTokenAsync();
        _loadCts.Dispose();
        _logsCts.Dispose();
        _workloadLogsCts.Dispose();
        _selectedResourceActionCts.Dispose();
    }

    partial void OnSelectedContextChanged(string value)
    {
        if (_suppressSelectionSideEffects || !_loaded || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _ = HandleContextChangedAsync(value);
    }

    partial void OnSelectedNamespaceChanged(string value)
    {
        OnPropertyChanged(nameof(EventsSummary));
        SyncMonitorNamespaceSelectionFromScope(value);

        if (_suppressSelectionSideEffects || !_loaded || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _ = HandleNamespaceChangedAsync(value);
    }

    partial void OnClientChanged(IAksClient? value)
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ShowPodEmptyState));
        OnPropertyChanged(nameof(CanStartSelectedPodPortForward));
        OnPropertyChanged(nameof(CanOpenSelectedPodShell));
        OnPropertyChanged(nameof(ResourceEmptyStateVisibility));

        if (value is null && SelectedPod is not null)
        {
            SelectedPod = null;
        }

        if (value is null)
        {
            Events.Clear();
            EventsErrorMessage = null;
            ClearResourceExplorerState();
        }
    }

    partial void OnShowEventsChanged(bool value)
    {
        OnPropertyChanged(nameof(EventsSectionVisibility));
        OnPropertyChanged(nameof(ToggleEventsButtonText));
        OnPropertyChanged(nameof(ShowEventsEmptyState));
    }

    partial void OnEventsErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(EventsErrorVisibility));
        OnPropertyChanged(nameof(ShowEventsEmptyState));
    }

    [RelayCommand]
    private void ToggleEvents()
    {
        ShowEvents = !ShowEvents;
    }

    private void ApplyBootstrapResult(AksClientBootstrapResult result)
    {
        Client = result.Client;

        ContextOptions.Clear();
        foreach (var context in result.Contexts.Select(context => context.Name))
        {
            ContextOptions.Add(context);
        }

        NamespaceOptions.Clear();
        foreach (var ns in BuildNamespaceOptions(result.Namespaces, result.CurrentNamespace))
        {
            NamespaceOptions.Add(ns);
        }

        SyncMonitorNamespaceOptions(result.Namespaces, result.CurrentNamespace);

        _suppressSelectionSideEffects = true;
        try
        {
            SelectedContext = result.ActiveContext;
            SelectedNamespace = result.CurrentNamespace;
        }
        finally
        {
            _suppressSelectionSideEffects = false;
        }

        ConnectionSummary = result.Status switch
        {
            AksClientBootstrapStatus.Connected when _appState.UseDemoData => $"Connected to demo cluster in namespace '{result.CurrentNamespace}'.",
            AksClientBootstrapStatus.Connected => $"Connected to '{result.ActiveContext}' in namespace '{result.CurrentNamespace}'.",
            AksClientBootstrapStatus.NotConfigured => "No AKS configuration found. Configure kubeconfig settings in Settings before opening this workspace.",
            _ => result.ErrorMessage ?? "AKS bootstrap failed."
        };
    }

    private void SeedSelectionFromConfig()
    {
        if (_hasSeededSelectionFromConfig || _appState.Config.AksConfig is not AksConfig config)
        {
            return;
        }

        _suppressSelectionSideEffects = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(config.KubeconfigContext))
            {
                SelectedContext = config.KubeconfigContext;
            }

            if (!string.IsNullOrWhiteSpace(config.DefaultNamespace))
            {
                SelectedNamespace = config.DefaultNamespace;
            }
        }
        finally
        {
            _suppressSelectionSideEffects = false;
        }

        _hasSeededSelectionFromConfig = true;
    }

    private async Task HandleContextChangedAsync(string context)
    {
        try
        {
            if (_appState.Config.AksConfig is AksConfig config)
            {
                config.KubeconfigContext = context;
                await _appState.SaveConfigAsync();
            }

            await ReloadAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError(ex, "AKS context switch failed.");
        }
    }

    private async Task HandleNamespaceChangedAsync(string ns)
    {
        try
        {
            if (_appState.Config.AksConfig is AksConfig config)
            {
                config.DefaultNamespace = ns;
                await _appState.SaveConfigAsync();
            }

            await SuspendSelectedPodLogsForReloadAsync("Loading pods for the new namespace...");
            await SuspendSelectedWorkloadLogsForReloadAsync("Loading pods for the new namespace...");
            Pods.Clear();
            OnPropertyChanged(nameof(PodCountLabel));
            OnPropertyChanged(nameof(ShowPodEmptyState));
            await ResetLoadTokenAsync();
            IsLoading = true;
            ErrorMessage = null;
            await Task.Yield();
            await LoadPodsAsync(_loadCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ClearResourceExplorerState();
            SelectedPod = null;
            ErrorMessage = ex.Message;
            _logger.LogError(ex, "AKS namespace switch failed.");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowPodEmptyState));
        }
    }

    private Task LoadPodsAsync(CancellationToken ct) => LoadResourceScopeAsync(ct);

    private async Task ResetLoadTokenAsync()
    {
        if (!_loadCts.IsCancellationRequested)
        {
            await _loadCts.CancelAsync();
        }

        _loadCts.Dispose();
        _loadCts = new CancellationTokenSource();
    }

    private static IEnumerable<string> BuildNamespaceOptions(IReadOnlyList<string> namespaces, string currentNamespace)
    {
        if (currentNamespace == "*")
        {
            yield return "*";
        }

        foreach (var ns in namespaces.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            yield return ns;
        }

        if (!string.IsNullOrWhiteSpace(currentNamespace) && currentNamespace != "*" &&
            !namespaces.Contains(currentNamespace, StringComparer.Ordinal))
        {
            yield return currentNamespace;
        }
    }
}

public sealed class AksPodItemViewModel
{
    public AksPodItemViewModel(PodInfo pod)
    {
        Name = pod.Name;
        Namespace = pod.Namespace;
        Containers = [.. pod.Containers];
        Status = string.IsNullOrWhiteSpace(pod.Status) ? pod.Phase : pod.Status;
        Ready = pod.ReadyDisplay;
        Restarts = pod.RestartCount.ToString();
        Node = string.IsNullOrWhiteSpace(pod.NodeName) ? "—" : pod.NodeName;
        Health = ResolveHealth(pod);
    }

    public string Name { get; }

    public string Namespace { get; }

    public IReadOnlyList<string> Containers { get; }

    public bool HasContainers => Containers.Count > 0;

    public string Health { get; }

    public string Status { get; }

    public string Ready { get; }

    public string Restarts { get; }

    public string Node { get; }

    private static string ResolveHealth(PodInfo pod)
    {
        var status = string.IsNullOrWhiteSpace(pod.Status) ? pod.Phase : pod.Status;

        if (pod.Ready && string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase))
        {
            return "Healthy";
        }

        if (status.Contains("CrashLoop", StringComparison.OrdinalIgnoreCase)
            || status.Contains("ImagePull", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Error", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Failed", StringComparison.OrdinalIgnoreCase)
            || status.Contains("OOMKilled", StringComparison.OrdinalIgnoreCase))
        {
            return "Error";
        }

        if (!pod.Ready
            || status.Contains("Pending", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Terminating", StringComparison.OrdinalIgnoreCase)
            || pod.RestartCount > 0)
        {
            return "Warning";
        }

        return "Unknown";
    }
}

public sealed class AksClusterEventItemViewModel
{
    public AksClusterEventItemViewModel(KubernetesEvent clusterEvent)
    {
        Type = string.IsNullOrWhiteSpace(clusterEvent.Type) ? "Normal" : clusterEvent.Type;
        Reason = string.IsNullOrWhiteSpace(clusterEvent.Reason) ? "Event" : clusterEvent.Reason;
        Message = string.IsNullOrWhiteSpace(clusterEvent.Message) ? "No event message was surfaced." : clusterEvent.Message;
        InvolvedObject = string.IsNullOrWhiteSpace(clusterEvent.InvolvedObjectKind)
            ? clusterEvent.InvolvedObjectName ?? "Unknown target"
            : string.IsNullOrWhiteSpace(clusterEvent.InvolvedObjectName)
                ? clusterEvent.InvolvedObjectKind
                : $"{clusterEvent.InvolvedObjectKind}/{clusterEvent.InvolvedObjectName}";
        CountLabel = clusterEvent.Count <= 1 ? "1 occurrence" : $"{clusterEvent.Count:N0} occurrences";
        TimestampText = clusterEvent.LastTimestamp?.ToLocalTime().ToString("g") ?? "Timestamp unavailable";
        IsWarning = string.Equals(Type, "Warning", StringComparison.OrdinalIgnoreCase);
    }

    public string Type { get; }

    public string Reason { get; }

    public string Message { get; }

    public string InvolvedObject { get; }

    public string CountLabel { get; }

    public string TimestampText { get; }

    public bool IsWarning { get; }
}