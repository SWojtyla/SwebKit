using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using SwebKit.Core.Models;

namespace SwebKit.WinUI.ViewModels.Aks;

public sealed partial class AksPageViewModel
{
    private const string EmptyWorkloadLogsMessage = "Open all-pod logs from a deployment or statefulset to stream aggregated workload diagnostics.";

    private static readonly IReadOnlyList<AksLogRangeOption> WorkloadLogRangeOptionsInternal =
    [
        new(LogRangeLastFiveMinutes, "Last 5m"),
        new(LogRangeLastHour, "Last 1h"),
        new(LogRangeAllBuffered, "All buffered"),
    ];

    private readonly List<AggregatedLogLine> _selectedWorkloadLogBuffer = [];
    private CancellationTokenSource _workloadLogsCts = new();
    private bool _workloadLogTextRefreshQueued;
    private string? _activeWorkloadLogSignature;
    private string? _selectedWorkloadLogNamespace;
    private string? _selectedWorkloadLogName;
    private string? _selectedWorkloadLogApiKind;

    public IReadOnlyList<AksLogRangeOption> WorkloadLogRangeOptions => WorkloadLogRangeOptionsInternal;

    [ObservableProperty]
    public partial bool IsSelectedWorkloadLogsOpen { get; set; }

    [ObservableProperty]
    public partial bool IsSelectedWorkloadLogsLoading { get; set; }

    [ObservableProperty]
    public partial string SelectedWorkloadLogsTitle { get; set; } = "Workload diagnostics";

    [ObservableProperty]
    public partial string SelectedWorkloadLogsStatus { get; set; } = EmptyWorkloadLogsMessage;

    [ObservableProperty]
    public partial string SelectedWorkloadLogsText { get; set; } = EmptyWorkloadLogsMessage;

    [ObservableProperty]
    public partial string? SelectedWorkloadLogsErrorMessage { get; set; }

    [ObservableProperty]
    public partial string SelectedWorkloadLogRange { get; set; } = LogRangeLastFiveMinutes;

    [ObservableProperty]
    public partial bool FollowSelectedWorkloadLogs { get; set; } = true;

    [ObservableProperty]
    public partial string WorkloadLogFilterText { get; set; } = string.Empty;

    public Visibility SelectedWorkloadLogsPanelVisibility => IsSelectedWorkloadLogsOpen || IsSelectedWorkloadLogsLoading || !string.IsNullOrWhiteSpace(SelectedWorkloadLogsErrorMessage)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility SelectedWorkloadLogsErrorVisibility => string.IsNullOrWhiteSpace(SelectedWorkloadLogsErrorMessage)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public bool CanReloadSelectedWorkloadLogs => IsSelectedWorkloadLogsOpen && !IsSelectedWorkloadLogsLoading && Client is not null;

    public bool CanToggleSelectedWorkloadLogsLive => IsSelectedWorkloadLogsOpen && !IsSelectedWorkloadLogsLoading;

    partial void OnIsSelectedWorkloadLogsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectedWorkloadLogsPanelVisibility));
        OnPropertyChanged(nameof(CanReloadSelectedWorkloadLogs));
        OnPropertyChanged(nameof(CanOpenSelectedResourceWorkloadLogs));
    }

    partial void OnSelectedWorkloadLogsErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(SelectedWorkloadLogsErrorVisibility));
        OnPropertyChanged(nameof(SelectedWorkloadLogsPanelVisibility));
    }

    partial void OnIsSelectedWorkloadLogsOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectedWorkloadLogsPanelVisibility));
        OnPropertyChanged(nameof(CanReloadSelectedWorkloadLogs));
    }

    partial void OnSelectedWorkloadLogRangeChanged(string value)
    {
        if (!_loaded || !IsSelectedWorkloadLogsOpen || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _ = StartSelectedWorkloadLogsAsync();
    }

    partial void OnFollowSelectedWorkloadLogsChanged(bool value)
    {
        if (!_loaded || !IsSelectedWorkloadLogsOpen)
        {
            return;
        }

        _ = StartSelectedWorkloadLogsAsync();
    }

    partial void OnWorkloadLogFilterTextChanged(string value)
    {
        RefreshSelectedWorkloadLogsText();
    }

    [RelayCommand]
    private async Task OpenSelectedResourceWorkloadLogsAsync()
    {
        var resource = SelectedResourceItem;
        if (resource is null || Client is null || !CanResourceSupportWorkloadLogs(resource))
        {
            return;
        }

        _selectedWorkloadLogNamespace = resource.Namespace;
        _selectedWorkloadLogName = resource.Name;
        _selectedWorkloadLogApiKind = resource.ApiKind;
        IsSelectedWorkloadLogsOpen = true;
        SelectedWorkloadLogsTitle = BuildSelectedWorkloadLogsTitle();
        await StartSelectedWorkloadLogsAsync();
    }

    [RelayCommand]
    private Task ReloadSelectedWorkloadLogsAsync() => StartSelectedWorkloadLogsAsync();

    [RelayCommand]
    private async Task CloseSelectedWorkloadLogsAsync()
    {
        await ResetWorkloadLogsTokenAsync();
        ResetSelectedWorkloadLogsState(clearTarget: true, closePanel: true);
    }

    private async Task StartSelectedWorkloadLogsAsync()
    {
        var client = Client;
        var workloadNamespace = _selectedWorkloadLogNamespace;
        var workloadName = _selectedWorkloadLogName;

        if (client is null || string.IsNullOrWhiteSpace(workloadNamespace) || string.IsNullOrWhiteSpace(workloadName))
        {
            ResetSelectedWorkloadLogsState(clearTarget: false, closePanel: false);
            return;
        }

        await ResetWorkloadLogsTokenAsync();

        var options = BuildSelectedWorkloadLogOptions();
        var signature = BuildWorkloadLogSignature(workloadNamespace, workloadName, SelectedWorkloadLogRange, options.Follow);
        var logToken = _workloadLogsCts.Token;

        _activeWorkloadLogSignature = signature;
        _selectedWorkloadLogBuffer.Clear();
        IsSelectedWorkloadLogsOpen = true;
        IsSelectedWorkloadLogsLoading = true;
        SelectedWorkloadLogsTitle = BuildSelectedWorkloadLogsTitle();
        SelectedWorkloadLogsErrorMessage = null;
        UpdateSelectedWorkloadLogsStatus(options);
        RefreshSelectedWorkloadLogsText();

        try
        {
            await foreach (var line in client.StreamDeploymentLogsAsync(
                workloadNamespace,
                workloadName,
                options,
                logToken).WithCancellation(logToken))
            {
                ExecuteOnUiThread(() =>
                {
                    if (!string.Equals(_activeWorkloadLogSignature, signature, StringComparison.Ordinal))
                    {
                        return;
                    }

                    AppendSelectedWorkloadLogLine(line);
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ExecuteOnUiThread(() =>
            {
                if (!string.Equals(_activeWorkloadLogSignature, signature, StringComparison.Ordinal))
                {
                    return;
                }

                SelectedWorkloadLogsErrorMessage = ex.Message;
                UpdateSelectedWorkloadLogsStatus(options);
                RefreshSelectedWorkloadLogsText();
            });
            _logger.LogError(ex, "AKS workload log stream failed for {Namespace}/{WorkloadName}.", workloadNamespace, workloadName);
        }
        finally
        {
            ExecuteOnUiThread(() =>
            {
                if (!string.Equals(_activeWorkloadLogSignature, signature, StringComparison.Ordinal))
                {
                    return;
                }

                IsSelectedWorkloadLogsLoading = false;
                UpdateSelectedWorkloadLogsStatus(options);
                RefreshSelectedWorkloadLogsText();
                OnPropertyChanged(nameof(CanReloadSelectedWorkloadLogs));
            });
        }
    }

    private async Task SuspendSelectedWorkloadLogsForReloadAsync(string statusMessage)
    {
        if (!IsSelectedWorkloadLogsOpen && string.IsNullOrWhiteSpace(_selectedWorkloadLogName))
        {
            return;
        }

        await ResetWorkloadLogsTokenAsync();

        ExecuteOnUiThread(() =>
        {
            _activeWorkloadLogSignature = null;
            _selectedWorkloadLogBuffer.Clear();
            _workloadLogTextRefreshQueued = false;
            IsSelectedWorkloadLogsLoading = false;
            SelectedWorkloadLogsErrorMessage = null;
            SelectedWorkloadLogsStatus = statusMessage;
            SelectedWorkloadLogsText = statusMessage;
        });
    }

    private async Task ResetWorkloadLogsTokenAsync()
    {
        if (!_workloadLogsCts.IsCancellationRequested)
        {
            await _workloadLogsCts.CancelAsync();
        }

        _workloadLogsCts.Dispose();
        _workloadLogsCts = new CancellationTokenSource();
    }

    private void ResetSelectedWorkloadLogsState(bool clearTarget, bool closePanel)
    {
        _activeWorkloadLogSignature = null;
        _workloadLogTextRefreshQueued = false;
        _selectedWorkloadLogBuffer.Clear();
        IsSelectedWorkloadLogsLoading = false;
        IsSelectedWorkloadLogsOpen = !closePanel;
        SelectedWorkloadLogsErrorMessage = null;
        SelectedWorkloadLogsStatus = EmptyWorkloadLogsMessage;
        SelectedWorkloadLogsText = EmptyWorkloadLogsMessage;
        SelectedWorkloadLogsTitle = clearTarget ? "Workload diagnostics" : BuildSelectedWorkloadLogsTitle();
        WorkloadLogFilterText = string.Empty;

        if (clearTarget)
        {
            _selectedWorkloadLogNamespace = null;
            _selectedWorkloadLogName = null;
            _selectedWorkloadLogApiKind = null;
        }
    }

    private void AppendSelectedWorkloadLogLine(AggregatedLogLine line)
    {
        _selectedWorkloadLogBuffer.Add(line);

        var overflow = _selectedWorkloadLogBuffer.Count - ResolveSelectedPodLogBufferSize();
        if (overflow > 0)
        {
            _selectedWorkloadLogBuffer.RemoveRange(0, overflow);
        }

        QueueSelectedWorkloadLogsTextRefresh();
    }

    private void RefreshSelectedWorkloadLogsText()
    {
        if (_selectedWorkloadLogBuffer.Count == 0)
        {
            SelectedWorkloadLogsText = IsSelectedWorkloadLogsLoading
                ? "Connecting to aggregated workload logs..."
                : string.IsNullOrWhiteSpace(SelectedWorkloadLogsErrorMessage)
                    ? "No aggregated log lines were returned for the current workload selection."
                    : "No aggregated log lines are available because the stream failed before any output arrived.";
            return;
        }

        IEnumerable<AggregatedLogLine> lines = _selectedWorkloadLogBuffer;
        if (!string.IsNullOrWhiteSpace(WorkloadLogFilterText))
        {
            lines = lines.Where(line => line.PodName.Contains(WorkloadLogFilterText, StringComparison.OrdinalIgnoreCase)
                || line.Line.Contains(WorkloadLogFilterText, StringComparison.OrdinalIgnoreCase));
        }

        var filteredLines = lines.ToList();
        if (filteredLines.Count == 0)
        {
            SelectedWorkloadLogsText = "No aggregated log lines match the current filter.";
            return;
        }

        if (filteredLines.Count > VisiblePodLogLineLimit)
        {
            filteredLines = filteredLines.Skip(filteredLines.Count - VisiblePodLogLineLimit).ToList();
        }

        SelectedWorkloadLogsText = string.Join(
            Environment.NewLine,
            filteredLines.Select(line => $"[{line.PodName}] {line.Line}"));
    }

    private void UpdateSelectedWorkloadLogsStatus(LogStreamOptions options)
    {
        var modeLabel = options.Follow ? "live stream" : "snapshot";
        var kindLabel = string.IsNullOrWhiteSpace(_selectedWorkloadLogApiKind) ? "Workload" : _selectedWorkloadLogApiKind;
        var nameLabel = string.IsNullOrWhiteSpace(_selectedWorkloadLogName) ? "selection" : _selectedWorkloadLogName;
        var namespaceLabel = string.IsNullOrWhiteSpace(_selectedWorkloadLogNamespace) ? "cluster" : _selectedWorkloadLogNamespace;

        SelectedWorkloadLogsStatus = $"{kindLabel} · {namespaceLabel}/{nameLabel} · {ResolveSelectedPodLogRangeLabel(SelectedWorkloadLogRange)} · {modeLabel}";
    }

    private LogStreamOptions BuildSelectedWorkloadLogOptions()
    {
        return SelectedWorkloadLogRange switch
        {
            LogRangeLastHour => new LogStreamOptions
            {
                SinceSeconds = 60 * 60,
                Follow = FollowSelectedWorkloadLogs,
            },
            LogRangeAllBuffered => new LogStreamOptions
            {
                TailLines = ResolveSelectedPodLogBufferSize(),
                Follow = FollowSelectedWorkloadLogs,
            },
            _ => new LogStreamOptions
            {
                SinceSeconds = 5 * 60,
                Follow = FollowSelectedWorkloadLogs,
            },
        };
    }

    private void QueueSelectedWorkloadLogsTextRefresh()
    {
        if (_workloadLogTextRefreshQueued)
        {
            return;
        }

        _workloadLogTextRefreshQueued = true;
        _ = FlushSelectedWorkloadLogsTextAsync();
    }

    private async Task FlushSelectedWorkloadLogsTextAsync()
    {
        await Task.Delay(150);

        ExecuteOnUiThread(() =>
        {
            _workloadLogTextRefreshQueued = false;
            RefreshSelectedWorkloadLogsText();
        });
    }

    private string BuildSelectedWorkloadLogsTitle()
    {
        if (string.IsNullOrWhiteSpace(_selectedWorkloadLogNamespace) || string.IsNullOrWhiteSpace(_selectedWorkloadLogName))
        {
            return "Workload diagnostics";
        }

        var kindLabel = string.IsNullOrWhiteSpace(_selectedWorkloadLogApiKind) ? "Workload" : _selectedWorkloadLogApiKind;
        return $"{kindLabel} diagnostics · {_selectedWorkloadLogNamespace}/{_selectedWorkloadLogName}";
    }

    private static bool CanResourceSupportWorkloadLogs(AksResourceBrowseItemViewModel resource)
        => string.Equals(resource.ApiKind, "Deployment", StringComparison.Ordinal)
            || string.Equals(resource.ApiKind, "StatefulSet", StringComparison.Ordinal);

    private static string BuildWorkloadLogSignature(string workloadNamespace, string workloadName, string rangeKey, bool follow)
        => $"{workloadNamespace}|{workloadName}|{rangeKey}|{follow}";
}
