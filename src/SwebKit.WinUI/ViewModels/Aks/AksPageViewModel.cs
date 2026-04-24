using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;

namespace SwebKit.WinUI.ViewModels.Aks;

public sealed partial class AksPageViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AppStateService _appState;
    private readonly IAksClientBootstrapper _bootstrapper;
    private readonly IShellNavigationService _navigation;
    private readonly IPortForwardSessionService _portForwardSessions;
    private readonly INotificationService _notifications;
    private readonly ILogger<AksPageViewModel> _logger;
    private CancellationTokenSource _loadCts = new();
    private bool _loaded;
    private bool _suppressSelectionSideEffects;

    public AksPageViewModel(
        AppStateService appState,
        IAksClientBootstrapper bootstrapper,
        IShellNavigationService navigation,
        IPortForwardSessionService portForwardSessions,
        INotificationService notifications,
        ILogger<AksPageViewModel> logger)
    {
        _appState = appState;
        _bootstrapper = bootstrapper;
        _navigation = navigation;
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

        InitializePortForwardState();
    }

    public ObservableCollection<string> ContextOptions { get; } = [];

    public ObservableCollection<string> NamespaceOptions { get; } = [];

    public ObservableCollection<AksPodItemViewModel> Pods { get; } = [];

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

    public bool IsConfigured => _appState.UseDemoData || _appState.Config.AksConfig is not null;

    public bool IsConnected => Client is not null;

    public bool HasContextOptions => ContextOptions.Count > 0;

    public bool HasNamespaceOptions => NamespaceOptions.Count > 0;

    public bool ShowNotConfiguredState => !IsLoading && !IsConfigured;

    public bool ShowPodEmptyState => IsConnected && !IsLoading && Pods.Count == 0 && ErrorMessage is null;

    public string PodCountLabel => Pods.Count == 1 ? "1 pod" : $"{Pods.Count} pods";

    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        await _appState.WhenInitializedAsync();

        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(ShowNotConfiguredState));

        if (!IsConfigured)
        {
            Client = null;
            ConnectionSummary = "No AKS configuration found. Configure kubeconfig settings in Settings before opening this workspace.";
            ErrorMessage = null;
            ContextOptions.Clear();
            NamespaceOptions.Clear();
            Pods.Clear();
            SelectedPod = null;
            SelectedContext = string.Empty;
            SelectedNamespace = string.Empty;
            OnPropertyChanged(nameof(IsConnected));
            return;
        }

        await SuspendSelectedPodLogsForReloadAsync("Reloading AKS scope...");
        Pods.Clear();
        OnPropertyChanged(nameof(PodCountLabel));
        OnPropertyChanged(nameof(ShowPodEmptyState));
        await ResetLoadTokenAsync();
        IsLoading = true;
        ErrorMessage = null;
        await Task.Yield();

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
                SelectedPod = null;
                ErrorMessage = result.ErrorMessage;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
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
        _navigation.NavigateTo("settings");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        DisposePortForwardState();
        await ResetLoadTokenAsync();
        await ResetLogsTokenAsync();
        _loadCts.Dispose();
        _logsCts.Dispose();
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

        if (value is null && SelectedPod is not null)
        {
            SelectedPod = null;
        }
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

    private async Task LoadPodsAsync(CancellationToken ct)
    {
        Pods.Clear();

        if (Client is null)
        {
            SelectedPod = null;
            return;
        }

        IReadOnlyList<PodInfo> pods;
        if (SelectedNamespace == "*")
        {
            var namespaces = NamespaceOptions.Where(option => option != "*").ToList();
            pods = namespaces.Count == 0
                ? []
                : await Client.GetPodsAsync(namespaces, ct);
        }
        else
        {
            pods = await Client.GetPodsAsync(SelectedNamespace, labelSelector: null, ct);
        }

        foreach (var pod in pods
            .OrderBy(pod => pod.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pod => pod.Name, StringComparer.OrdinalIgnoreCase))
        {
            Pods.Add(new AksPodItemViewModel(pod));
        }

        ReconcileSelectedPodAfterLoad();

        OnPropertyChanged(nameof(PodCountLabel));
        OnPropertyChanged(nameof(ShowPodEmptyState));
    }

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