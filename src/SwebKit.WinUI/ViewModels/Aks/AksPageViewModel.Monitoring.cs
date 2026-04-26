using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using SwebKit.Core.Models;

namespace SwebKit.WinUI.ViewModels.Aks;

public sealed partial class AksPageViewModel
{
    public ObservableCollection<string> MonitorNamespaceOptions { get; } = [];

    public ObservableCollection<AksMonitoredNamespaceItemViewModel> MonitoredNamespaces { get; } = [];

    public ObservableCollection<AksPodHealthAlertItemViewModel> PodHealthAlerts { get; } = [];

    [ObservableProperty]
    public partial bool IsMonitorPanelOpen { get; set; }

    [ObservableProperty]
    public partial bool IsMonitoring { get; set; }

    [ObservableProperty]
    public partial string SelectedMonitorNamespace { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MonitorNamespaceFilter { get; set; } = string.Empty;

    public bool HasMonitorNamespaceOptions => MonitorNamespaceOptions.Count > 0;

    public bool HasMonitoredNamespaces => MonitoredNamespaces.Count > 0;

    public bool HasPodHealthAlerts => PodHealthAlerts.Count > 0;

    public IReadOnlyList<string> FilteredMonitorNamespaceOptions => string.IsNullOrWhiteSpace(MonitorNamespaceFilter)
        ? [.. MonitorNamespaceOptions]
        : [.. MonitorNamespaceOptions.Where(ns => ns.Contains(MonitorNamespaceFilter.Trim(), StringComparison.OrdinalIgnoreCase))];

    public bool CanAddSelectedMonitorNamespace =>
        !string.IsNullOrWhiteSpace(SelectedMonitorNamespace)
        && !MonitoredNamespaces.Any(item => string.Equals(item.Name, SelectedMonitorNamespace, StringComparison.OrdinalIgnoreCase));

    public bool CanStartMonitoring => HasMonitoredNamespaces && !IsMonitoring;

    public bool CanStopMonitoring => IsMonitoring;

    public string MonitorButtonText => IsMonitoring
        ? $"Monitor ({MonitoredNamespaces.Count.ToString(CultureInfo.CurrentCulture)})"
        : "Monitor";

    public string MonitorSummary => !IsMonitoring
        ? "Not monitoring any namespaces yet."
        : $"Monitoring {MonitoredNamespaces.Count.ToString(CultureInfo.CurrentCulture)} namespace{(MonitoredNamespaces.Count == 1 ? string.Empty : "s")} for pod-health regressions.";

    public string MonitorPanelDescription => _appState.UseDemoData
        ? "Demo mode uses the same monitoring workflow and persisted state, so it doubles as a showcase and a fast parity test path."
        : "Match the MAUI monitor workflow here by managing watched namespaces and reviewing recent pod-health alerts without leaving AKS.";

    public bool ShowMonitorDemoInfo => _appState.UseDemoData;

    public bool ShowMonitorNamespacesEmptyState => !HasMonitoredNamespaces;

    public bool ShowNoPodHealthAlerts => !HasPodHealthAlerts;

    public Visibility MonitorPanelVisibility => IsMonitorPanelOpen ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StartMonitoringVisibility => IsMonitoring ? Visibility.Collapsed : Visibility.Visible;

    public Visibility StopMonitoringVisibility => IsMonitoring ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MonitoredNamespacesVisibility => HasMonitoredNamespaces ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PodHealthAlertsVisibility => HasPodHealthAlerts ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsMonitorPanelOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(MonitorPanelVisibility));
    }

    partial void OnIsMonitoringChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartMonitoring));
        OnPropertyChanged(nameof(CanStopMonitoring));
        OnPropertyChanged(nameof(MonitorButtonText));
        OnPropertyChanged(nameof(MonitorSummary));
        OnPropertyChanged(nameof(StartMonitoringVisibility));
        OnPropertyChanged(nameof(StopMonitoringVisibility));
    }

    partial void OnSelectedMonitorNamespaceChanged(string value)
    {
        OnPropertyChanged(nameof(CanAddSelectedMonitorNamespace));
    }

    partial void OnMonitorNamespaceFilterChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredMonitorNamespaceOptions));
        EnsureSelectedMonitorNamespace();
    }

    private void InitializeMonitoringState()
    {
        MonitorNamespaceOptions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMonitorNamespaceOptions));
            OnPropertyChanged(nameof(FilteredMonitorNamespaceOptions));
        };

        MonitoredNamespaces.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMonitoredNamespaces));
            OnPropertyChanged(nameof(CanAddSelectedMonitorNamespace));
            OnPropertyChanged(nameof(CanStartMonitoring));
            OnPropertyChanged(nameof(MonitorButtonText));
            OnPropertyChanged(nameof(MonitorSummary));
            OnPropertyChanged(nameof(ShowMonitorNamespacesEmptyState));
            OnPropertyChanged(nameof(MonitoredNamespacesVisibility));
        };

        PodHealthAlerts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPodHealthAlerts));
            OnPropertyChanged(nameof(ShowNoPodHealthAlerts));
            OnPropertyChanged(nameof(PodHealthAlertsVisibility));
        };

        _monitor.MonitoringStateChanged += OnMonitoringStateChanged;
        _monitor.PodHealthDetected += OnMonitorPodHealthDetected;
    }

    private void DisposeMonitoringState()
    {
        _monitor.MonitoringStateChanged -= OnMonitoringStateChanged;
        _monitor.PodHealthDetected -= OnMonitorPodHealthDetected;
    }

    private void OnMonitoringStateChanged()
    {
        ExecuteOnUiThread(SyncMonitoringState);
    }

    private void OnMonitorPodHealthDetected(PodHealthEvent evt)
    {
        ExecuteOnUiThread(() =>
        {
            SyncMonitoringState();
            SyncPodHealthAlerts();
        });
    }

    [RelayCommand]
    private void ToggleMonitorPanel()
    {
        IsMonitorPanelOpen = !IsMonitorPanelOpen;
    }

    [RelayCommand]
    private async Task AddSelectedMonitorNamespaceAsync()
    {
        var selectedNamespace = SelectedMonitorNamespace?.Trim();
        if (string.IsNullOrWhiteSpace(selectedNamespace))
        {
            return;
        }

        await _monitor.AddNamespaceAsync(selectedNamespace);
        SyncMonitoringState();
        _notifications.ShowSuccess("AKS monitoring namespace added", selectedNamespace);
    }

    [RelayCommand]
    private async Task RemoveMonitorNamespaceAsync(AksMonitoredNamespaceItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await _monitor.RemoveNamespaceAsync(item.Name);
        if (_monitor.IsMonitoring && _monitor.MonitoredNamespaces.Count == 0)
        {
            await _monitor.StopAsync();
        }

        SyncMonitoringState();
        SyncPodHealthAlerts();
        _notifications.ShowInfo("AKS monitoring namespace removed", item.Name);
    }

    [RelayCommand]
    private async Task StartMonitoringAsync()
    {
        if (!HasMonitoredNamespaces)
        {
            return;
        }

        await _monitor.StartAsync();
        SyncMonitoringState();
        _notifications.ShowSuccess(
            "AKS monitoring started",
            $"Watching {MonitoredNamespaces.Count.ToString(CultureInfo.CurrentCulture)} namespace{(MonitoredNamespaces.Count == 1 ? string.Empty : "s")}. ");
    }

    [RelayCommand]
    private async Task StopMonitoringAsync()
    {
        if (!_monitor.IsMonitoring)
        {
            return;
        }

        await _monitor.StopAsync();
        SyncMonitoringState();
        _notifications.ShowSuccess("AKS monitoring stopped");
    }

    private void SyncMonitoringState()
    {
        IsMonitoring = _monitor.IsMonitoring;
        ReplaceCollection(
            MonitoredNamespaces,
            _monitor.MonitoredNamespaces
                .OrderBy(static ns => ns, StringComparer.OrdinalIgnoreCase)
                .Select(static ns => new AksMonitoredNamespaceItemViewModel(ns)));
    }

    private void SyncPodHealthAlerts()
    {
        ReplaceCollection(
            PodHealthAlerts,
            _monitor.RecentEvents
                .OrderByDescending(static evt => evt.DetectedAt)
                .Take(8)
                .Select(static evt => new AksPodHealthAlertItemViewModel(evt)));
    }

    private void SyncMonitorNamespaceOptions(IReadOnlyList<string> namespaces, string currentNamespace)
    {
        ReplaceCollection(
            MonitorNamespaceOptions,
            namespaces
                .OrderBy(static ns => ns, StringComparer.OrdinalIgnoreCase)
                .Concat(string.IsNullOrWhiteSpace(currentNamespace) || currentNamespace == "*" || namespaces.Contains(currentNamespace, StringComparer.Ordinal)
                    ? []
                    : [currentNamespace]));

        SyncMonitorNamespaceSelectionFromScope(currentNamespace);
        EnsureSelectedMonitorNamespace();
    }

    private void ClearMonitorNamespaceOptions()
    {
        MonitorNamespaceOptions.Clear();
        SelectedMonitorNamespace = string.Empty;
        MonitorNamespaceFilter = string.Empty;
    }

    private void SyncMonitorNamespaceSelectionFromScope(string currentNamespace)
    {
        if (string.IsNullOrWhiteSpace(currentNamespace) || currentNamespace == "*")
        {
            EnsureSelectedMonitorNamespace();
            return;
        }

        if (MonitorNamespaceOptions.Contains(currentNamespace, StringComparer.Ordinal)
            && !string.Equals(SelectedMonitorNamespace, currentNamespace, StringComparison.Ordinal))
        {
            SelectedMonitorNamespace = currentNamespace;
        }
    }

    private void EnsureSelectedMonitorNamespace()
    {
        var availableOptions = FilteredMonitorNamespaceOptions;

        if (availableOptions.Count == 0)
        {
            if (!string.IsNullOrEmpty(SelectedMonitorNamespace))
            {
                SelectedMonitorNamespace = string.Empty;
            }

            return;
        }

        if (!availableOptions.Contains(SelectedMonitorNamespace, StringComparer.Ordinal))
        {
            SelectedMonitorNamespace = availableOptions[0];
        }
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}

public sealed class AksMonitoredNamespaceItemViewModel
{
    public AksMonitoredNamespaceItemViewModel(string name)
    {
        Name = name;
    }

    public string Name { get; }
}

public sealed class AksPodHealthAlertItemViewModel
{
    public AksPodHealthAlertItemViewModel(PodHealthEvent evt)
    {
        PodName = evt.PodName;
        Namespace = evt.Namespace;
        Status = ResolveStatus(evt.EventType);
        DetailText = string.IsNullOrWhiteSpace(evt.Message)
            ? evt.CurrentPhase ?? evt.EventType.ToString()
            : evt.Message!;
        TimestampText = FormatRelative(evt.DetectedAt);
    }

    public string PodName { get; }

    public string Namespace { get; }

    public string Status { get; }

    public string DetailText { get; }

    public string TimestampText { get; }

    private static string ResolveStatus(PodHealthEventType type) => type switch
    {
        PodHealthEventType.PodFailed => "Failed",
        PodHealthEventType.PodCrashLoop => "CrashLoop",
        PodHealthEventType.ContainerNotReady => "Not Ready",
        PodHealthEventType.PodUnknown => "Unknown",
        PodHealthEventType.PodTerminated => "Terminated",
        _ => type.ToString()
    };

    private static string FormatRelative(DateTimeOffset timestamp)
    {
        var delta = DateTimeOffset.UtcNow - timestamp;
        if (delta.TotalMinutes < 1)
        {
            return "just now";
        }

        if (delta.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)delta.TotalMinutes)}m ago";
        }

        if (delta.TotalDays < 1)
        {
            return $"{Math.Max(1, (int)delta.TotalHours)}h ago";
        }

        return $"{Math.Max(1, (int)delta.TotalDays)}d ago";
    }
}