using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Models;

namespace SwebKit.WinUI.ViewModels.Aks;

public sealed partial class AksPageViewModel
{
    private const string EmptyPodLogsMessage = "Select a pod from the list to stream container logs.";
    private const string LogRangeLastFiveMinutes = "last-5m";
    private const string LogRangeLastHour = "last-1h";
    private const string LogRangeAllBuffered = "all";
    private const string LogRangePreviousContainer = "previous";
    private const int VisiblePodLogLineLimit = 1_500;

    private static readonly IReadOnlyList<AksLogRangeOption> DefaultLogRangeOptions =
    [
        new(LogRangeLastFiveMinutes, "Last 5m"),
        new(LogRangeLastHour, "Last 1h"),
        new(LogRangeAllBuffered, "All buffered"),
        new(LogRangePreviousContainer, "Previous container"),
    ];

    private readonly SynchronizationContext _uiContext = SynchronizationContext.Current
        ?? throw new InvalidOperationException("AksPageViewModel requires a WinUI synchronization context.");
    private readonly List<string> _selectedPodLogBuffer = [];
    private CancellationTokenSource _logsCts = new();
    private bool _suppressPodLogSelectionSideEffects;
    private bool _logTextRefreshQueued;
    private string? _activePodLogSignature;

    public ObservableCollection<string> SelectedPodContainerOptions { get; } = [];

    public IReadOnlyList<AksLogRangeOption> LogRangeOptions => DefaultLogRangeOptions;

    [ObservableProperty]
    public partial AksPodItemViewModel? SelectedPod { get; set; }

    [ObservableProperty]
    public partial string SelectedPodLogContainer { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedPodLogRange { get; set; } = LogRangeLastFiveMinutes;

    [ObservableProperty]
    public partial bool FollowSelectedPodLogs { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSelectedPodLogsLoading { get; set; }

    [ObservableProperty]
    public partial string SelectedPodLogsStatus { get; set; } = EmptyPodLogsMessage;

    [ObservableProperty]
    public partial string SelectedPodLogsText { get; set; } = EmptyPodLogsMessage;

    [ObservableProperty]
    public partial string? SelectedPodLogsErrorMessage { get; set; }

    [ObservableProperty]
    public partial string LogFilterText { get; set; } = string.Empty;

    public bool HasMultipleSelectedPodContainers => SelectedPodContainerOptions.Count > 1;

    public bool CanInspectSelectedPodLogs => !IsLoading && Client is not null && SelectedPod is not null;

    public bool CanReloadSelectedPodLogs => CanInspectSelectedPodLogs && !IsSelectedPodLogsLoading;

    public bool CanClearSelectedPodSelection => SelectedPod is not null;

    public bool CanToggleSelectedPodLogsLive => CanInspectSelectedPodLogs && !IsPreviousContainerLogRangeSelected;

    partial void OnSelectedPodChanged(AksPodItemViewModel? value)
    {
        OnPropertyChanged(nameof(CanInspectSelectedPodLogs));
        OnPropertyChanged(nameof(CanReloadSelectedPodLogs));
        OnPropertyChanged(nameof(CanClearSelectedPodSelection));
        OnPropertyChanged(nameof(CanToggleSelectedPodLogsLive));

        if (!_loaded)
        {
            return;
        }

        if (value is null)
        {
            _ = ClearSelectedPodLogsAsync();
            return;
        }

        SyncSelectedPodContainers(value);
        _ = StartSelectedPodLogsAsync();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInspectSelectedPodLogs));
        OnPropertyChanged(nameof(CanReloadSelectedPodLogs));
        OnPropertyChanged(nameof(CanToggleSelectedPodLogsLive));
    }

    partial void OnSelectedPodLogContainerChanged(string value)
    {
        if (_suppressPodLogSelectionSideEffects || !_loaded || SelectedPod is null || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _ = StartSelectedPodLogsAsync();
    }

    partial void OnSelectedPodLogRangeChanged(string value)
    {
        OnPropertyChanged(nameof(CanToggleSelectedPodLogsLive));

        if (_suppressPodLogSelectionSideEffects || !_loaded || SelectedPod is null || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _ = StartSelectedPodLogsAsync();
    }

    partial void OnFollowSelectedPodLogsChanged(bool value)
    {
        if (_suppressPodLogSelectionSideEffects || !_loaded || SelectedPod is null)
        {
            return;
        }

        _ = StartSelectedPodLogsAsync();
    }

    partial void OnLogFilterTextChanged(string value)
    {
        RefreshSelectedPodLogsText();
    }

    [RelayCommand]
    private async Task SelectPodAsync(AksPodItemViewModel? pod)
    {
        if (pod is null)
        {
            return;
        }

        if (ReferenceEquals(SelectedPod, pod))
        {
            await StartSelectedPodLogsAsync();
            return;
        }

        SelectedPod = pod;
    }

    [RelayCommand]
    private Task ClearSelectedPodSelectionAsync()
    {
        if (SelectedPod is null)
        {
            return Task.CompletedTask;
        }

        SelectedPod = null;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ReloadSelectedPodLogsAsync() => StartSelectedPodLogsAsync();

    private async Task StartSelectedPodLogsAsync()
    {
        var selectedPod = SelectedPod;
        var client = Client;

        if (selectedPod is null || client is null)
        {
            await ClearSelectedPodLogsAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedPodLogContainer))
        {
            SyncSelectedPodContainers(selectedPod);
        }

        if (string.IsNullOrWhiteSpace(SelectedPodLogContainer))
        {
            SelectedPodLogsErrorMessage = "The selected pod does not expose any containers for log streaming.";
            SelectedPodLogsStatus = $"{selectedPod.Namespace}/{selectedPod.Name} has no containers available for log streaming.";
            SelectedPodLogsText = "No containers are available for the selected pod.";
            return;
        }

        await ResetLogsTokenAsync();

        var options = BuildSelectedPodLogOptions();
        var signature = BuildPodLogSignature(selectedPod, SelectedPodLogContainer, SelectedPodLogRange, options.Follow);
        var logToken = _logsCts.Token;

        _activePodLogSignature = signature;
        _selectedPodLogBuffer.Clear();
        IsSelectedPodLogsLoading = true;
        SelectedPodLogsErrorMessage = null;
        UpdateSelectedPodLogsStatus(selectedPod, SelectedPodLogContainer, options);
        RefreshSelectedPodLogsText();
        OnPropertyChanged(nameof(CanReloadSelectedPodLogs));

        try
        {
            await foreach (var line in client.StreamPodLogsAsync(
                selectedPod.Namespace,
                selectedPod.Name,
                SelectedPodLogContainer,
                options,
                logToken).WithCancellation(logToken))
            {
                ExecuteOnUiThread(() =>
                {
                    if (!string.Equals(_activePodLogSignature, signature, StringComparison.Ordinal))
                    {
                        return;
                    }

                    AppendSelectedPodLogLine(line);
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
                if (!string.Equals(_activePodLogSignature, signature, StringComparison.Ordinal))
                {
                    return;
                }

                SelectedPodLogsErrorMessage = ex.Message;
                UpdateSelectedPodLogsStatus(selectedPod, SelectedPodLogContainer, options);
                RefreshSelectedPodLogsText();
            });
            _logger.LogError(ex, "AKS pod log stream failed for {Namespace}/{PodName}.", selectedPod.Namespace, selectedPod.Name);
        }
        finally
        {
            ExecuteOnUiThread(() =>
            {
                if (!string.Equals(_activePodLogSignature, signature, StringComparison.Ordinal))
                {
                    return;
                }

                IsSelectedPodLogsLoading = false;
                UpdateSelectedPodLogsStatus(selectedPod, SelectedPodLogContainer, options);
                OnPropertyChanged(nameof(CanReloadSelectedPodLogs));
            });
        }
    }

    private async Task ClearSelectedPodLogsAsync()
    {
        await ResetLogsTokenAsync();
        ExecuteOnUiThread(ResetSelectedPodLogsState);
    }

    private void SyncSelectedPodContainers(AksPodItemViewModel selectedPod)
    {
        _suppressPodLogSelectionSideEffects = true;
        try
        {
            SelectedPodContainerOptions.Clear();
            foreach (var container in selectedPod.Containers)
            {
                SelectedPodContainerOptions.Add(container);
            }

            if (!selectedPod.Containers.Contains(SelectedPodLogContainer, StringComparer.Ordinal))
            {
                SelectedPodLogContainer = selectedPod.Containers.FirstOrDefault() ?? string.Empty;
            }
        }
        finally
        {
            _suppressPodLogSelectionSideEffects = false;
        }

        OnPropertyChanged(nameof(HasMultipleSelectedPodContainers));
    }

    private void ResetSelectedPodLogsState()
    {
        _activePodLogSignature = null;
        _logTextRefreshQueued = false;
        _selectedPodLogBuffer.Clear();
        IsSelectedPodLogsLoading = false;
        SelectedPodLogsErrorMessage = null;
        SelectedPodLogsStatus = EmptyPodLogsMessage;
        SelectedPodLogsText = EmptyPodLogsMessage;

        _suppressPodLogSelectionSideEffects = true;
        try
        {
            SelectedPodContainerOptions.Clear();
            SelectedPodLogContainer = string.Empty;
            LogFilterText = string.Empty;
        }
        finally
        {
            _suppressPodLogSelectionSideEffects = false;
        }

        OnPropertyChanged(nameof(HasMultipleSelectedPodContainers));
        OnPropertyChanged(nameof(CanReloadSelectedPodLogs));
        OnPropertyChanged(nameof(CanToggleSelectedPodLogsLive));
    }

    private void AppendSelectedPodLogLine(string line)
    {
        _selectedPodLogBuffer.Add(line);

        var overflow = _selectedPodLogBuffer.Count - ResolveSelectedPodLogBufferSize();
        if (overflow > 0)
        {
            _selectedPodLogBuffer.RemoveRange(0, overflow);
        }

        QueueSelectedPodLogsTextRefresh();
    }

    private void RefreshSelectedPodLogsText()
    {
        if (_selectedPodLogBuffer.Count == 0)
        {
            SelectedPodLogsText = SelectedPod is null
                ? EmptyPodLogsMessage
                : IsSelectedPodLogsLoading
                    ? "Connecting to pod log stream..."
                    : string.IsNullOrWhiteSpace(SelectedPodLogsErrorMessage)
                        ? "No log lines were returned for the current selection."
                        : "No log lines are available because the stream failed before any output arrived.";
            return;
        }

        IEnumerable<string> lines = _selectedPodLogBuffer;
        if (!string.IsNullOrWhiteSpace(LogFilterText))
        {
            lines = lines.Where(line => line.Contains(LogFilterText, StringComparison.OrdinalIgnoreCase));
        }

        var filteredLines = lines.ToList();
        if (filteredLines.Count == 0)
        {
            SelectedPodLogsText = "No log lines match the current filter.";
            return;
        }

        if (filteredLines.Count > VisiblePodLogLineLimit)
        {
            var visibleLines = filteredLines.Skip(filteredLines.Count - VisiblePodLogLineLimit).ToList();
            SelectedPodLogsText = $"[showing latest {VisiblePodLogLineLimit:N0} matching lines of {filteredLines.Count:N0}]"
                + Environment.NewLine
                + string.Join(Environment.NewLine, visibleLines);
            return;
        }

        SelectedPodLogsText = string.Join(Environment.NewLine, filteredLines);
    }

    private void UpdateSelectedPodLogsStatus(AksPodItemViewModel selectedPod, string containerName, LogStreamOptions options)
    {
        var modeLabel = options.PreviousContainer
            ? "previous container snapshot"
            : options.Follow
                ? "live stream"
                : "snapshot";
        var containerLabel = string.IsNullOrWhiteSpace(containerName) ? "default container" : containerName;

        SelectedPodLogsStatus = $"{selectedPod.Namespace}/{selectedPod.Name} · {containerLabel} · {ResolveSelectedPodLogRangeLabel(SelectedPodLogRange)} · {modeLabel}";
    }

    private LogStreamOptions BuildSelectedPodLogOptions()
    {
        var follow = FollowSelectedPodLogs && !IsPreviousContainerLogRangeSelected;

        return SelectedPodLogRange switch
        {
            LogRangeLastHour => new LogStreamOptions
            {
                SinceSeconds = 60 * 60,
                Follow = follow,
            },
            LogRangeAllBuffered => new LogStreamOptions
            {
                TailLines = ResolveSelectedPodLogBufferSize(),
                Follow = follow,
            },
            LogRangePreviousContainer => new LogStreamOptions
            {
                TailLines = ResolveSelectedPodLogBufferSize(),
                Follow = false,
                PreviousContainer = true,
            },
            _ => new LogStreamOptions
            {
                SinceSeconds = 5 * 60,
                Follow = follow,
            },
        };
    }

    private int ResolveSelectedPodLogBufferSize()
    {
        var configuredBufferSize = _appState.Config.AksConfig?.LogBufferSize ?? 10_000;
        return Math.Max(250, configuredBufferSize);
    }

    private bool IsPreviousContainerLogRangeSelected =>
        string.Equals(SelectedPodLogRange, LogRangePreviousContainer, StringComparison.Ordinal);

    private static string BuildPodLogSignature(AksPodItemViewModel selectedPod, string containerName, string rangeKey, bool follow)
        => $"{selectedPod.Namespace}|{selectedPod.Name}|{containerName}|{rangeKey}|{follow}";

    private static string ResolveSelectedPodLogRangeLabel(string rangeKey)
        => DefaultLogRangeOptions.FirstOrDefault(option => string.Equals(option.Key, rangeKey, StringComparison.Ordinal))?.Label
            ?? "Recent";

    private void ExecuteOnUiThread(Action action)
    {
        if (SynchronizationContext.Current == _uiContext)
        {
            action();
            return;
        }

        _uiContext.Post(_ => action(), null);
    }

    private void QueueSelectedPodLogsTextRefresh()
    {
        if (_logTextRefreshQueued)
        {
            return;
        }

        _logTextRefreshQueued = true;
        _ = FlushSelectedPodLogsTextAsync();
    }

    private async Task FlushSelectedPodLogsTextAsync()
    {
        await Task.Delay(150);

        ExecuteOnUiThread(() =>
        {
            _logTextRefreshQueued = false;
            RefreshSelectedPodLogsText();
        });
    }

    private void ReconcileSelectedPodAfterLoad()
    {
        if (SelectedPod is null)
        {
            return;
        }

        var matchingPod = Pods.FirstOrDefault(
            pod => string.Equals(pod.Namespace, SelectedPod.Namespace, StringComparison.Ordinal)
                && string.Equals(pod.Name, SelectedPod.Name, StringComparison.Ordinal));

        if (matchingPod is null)
        {
            SelectedPod = null;
            return;
        }

        if (!ReferenceEquals(matchingPod, SelectedPod))
        {
            SelectedPod = matchingPod;
        }
    }

    private async Task SuspendSelectedPodLogsForReloadAsync(string statusMessage)
    {
        if (SelectedPod is null)
        {
            return;
        }

        await ResetLogsTokenAsync();

        ExecuteOnUiThread(() =>
        {
            _activePodLogSignature = null;
            _selectedPodLogBuffer.Clear();
            IsSelectedPodLogsLoading = false;
            SelectedPodLogsErrorMessage = null;
            SelectedPodLogsStatus = statusMessage;
            SelectedPodLogsText = statusMessage;
            OnPropertyChanged(nameof(CanReloadSelectedPodLogs));
        });
    }

    private async Task ResetLogsTokenAsync()
    {
        if (!_logsCts.IsCancellationRequested)
        {
            await _logsCts.CancelAsync();
        }

        _logsCts.Dispose();
        _logsCts = new CancellationTokenSource();
    }
}

public sealed record AksLogRangeOption(string Key, string Label);
