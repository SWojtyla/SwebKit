using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.WinUI.ViewModels.Redis;

public sealed partial class RedisPageViewModel
{
    private readonly RedisOpsInsightsAggregator _opsInsightsAggregator;
    private readonly RedisKeyspaceHealthAnalyzer _healthAnalyzer = new();
    private readonly HashSet<string> _selectedKeys = new(StringComparer.Ordinal);
    private readonly List<RedisKeyInfo> _scannedKeyInfos = [];
    private CancellationTokenSource _insightsCts = new();
    private RedisKeyspaceHealthReport? _healthReport;

    public ObservableCollection<RedisHealthFindingItemViewModel> HealthFindings { get; } = [];

    public ObservableCollection<PrefixMemoryBucketItemViewModel> PrefixMemoryBuckets { get; } = [];

    public ObservableCollection<RedisSlowLogEntryItemViewModel> SlowLogEntries { get; } = [];

    public ObservableCollection<RedisHotKeySignalItemViewModel> HotKeySignals { get; } = [];

    public ObservableCollection<RedisPubSubChannelItemViewModel> PubSubChannels { get; } = [];

    [ObservableProperty]
    public partial bool IsAnalyzingHealth { get; set; }

    [ObservableProperty]
    public partial bool IsAnalyzingMemory { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingSlowLog { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingPubSub { get; set; }

    [ObservableProperty]
    public partial bool IsSelectionMode { get; set; }

    [ObservableProperty]
    public partial string BulkDeleteConfirmText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDeletingSelectedKeys { get; set; }

    [ObservableProperty]
    public partial string HealthCoverageText { get; set; } = "Run Analyze to inspect loaded keys for TTL, size, prefix, and hot-key risks.";

    [ObservableProperty]
    public partial string HealthSignalText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PrefixMemorySummaryText { get; set; } = "Run Analyze to inspect memory distribution by prefix.";

    [ObservableProperty]
    public partial string SlowLogSummaryText { get; set; } = "Load slowlog to inspect expensive Redis commands and inferred hot keys.";

    [ObservableProperty]
    public partial string PubSubSummaryText { get; set; } = "Load Pub/Sub to inspect active channels and subscription pressure.";

    [ObservableProperty]
    public partial RedisHealthSeverity? HealthSeverityFilter { get; set; }

    public bool HasHealthReport => _healthReport is not null;

    public bool ShowHealthEmptyState => !IsAnalyzingHealth && _healthReport is null;

    public bool ShowFilteredHealthEmptyState => _healthReport is not null && HealthFindings.Count == 0;

    public Visibility HealthFiltersVisibility => _healthReport is not null ? Visibility.Visible : Visibility.Collapsed;

    public bool ShowPrefixMemoryEmptyState => !IsAnalyzingMemory && PrefixMemoryBuckets.Count == 0;

    public bool ShowSlowLogEmptyState => !IsLoadingSlowLog && SlowLogEntries.Count == 0;

    public bool ShowHotKeySignalsEmptyState => !IsLoadingSlowLog && HotKeySignals.Count == 0;

    public bool ShowPubSubEmptyState => !IsLoadingPubSub && PubSubChannels.Count == 0;

    public bool CanAnalyzeHealth => IsConnected && _keys.Count > 0 && !IsWorking && !IsAnalyzingHealth;

    public bool CanAnalyzePrefixMemory => IsConnected && _keys.Count > 0 && !IsWorking && !IsAnalyzingMemory;

    public bool CanLoadSlowLog => IsConnected && !IsWorking && !IsLoadingSlowLog;

    public bool CanLoadPubSub => IsConnected && !IsWorking && !IsLoadingPubSub;

    public string HealthAllFilterLabel => _healthReport is null ? "All" : $"All ({_healthReport.Findings.Count})";

    public string HealthCriticalFilterLabel => $"Critical ({_healthReport?.CriticalCount ?? 0})";

    public string HealthWarningFilterLabel => $"Warning ({_healthReport?.WarningCount ?? 0})";

    public string HealthInfoFilterLabel => $"Info ({_healthReport?.InfoCount ?? 0})";

    public bool IsHealthAllFilterActive => HealthSeverityFilter is null;

    public bool IsHealthCriticalFilterActive => HealthSeverityFilter == RedisHealthSeverity.Critical;

    public bool IsHealthWarningFilterActive => HealthSeverityFilter == RedisHealthSeverity.Warning;

    public bool IsHealthInfoFilterActive => HealthSeverityFilter == RedisHealthSeverity.Info;

    public int SelectedKeyCount => _selectedKeys.Count;

    public string SelectionModeButtonLabel => IsSelectionMode ? "Done selecting" : "Select";

    public string SelectionSummaryText => _selectedKeys.Count switch
    {
        0 when _keys.Count == 0 => "No loaded keys are available for bulk actions.",
        0 => _hasMoreKeys
            ? $"No loaded keys selected. {_keys.Count} loaded match the current pattern, and more matches are available."
            : $"No loaded keys selected. {_keys.Count} loaded match the current pattern.",
        1 => _hasMoreKeys
            ? "1 loaded key selected. More matching keys are available but not loaded."
            : "1 loaded key selected.",
        _ => _hasMoreKeys
            ? $"{_selectedKeys.Count} loaded keys selected. More matching keys are available but not loaded."
            : $"{_selectedKeys.Count} loaded keys selected."
    };

            public Visibility HeaderBulkToolbarVisibility => (_keys.Count > 0 || IsSelectionMode) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SelectionSummaryVisibility => IsSelectionMode ? Visibility.Visible : Visibility.Collapsed;

    public bool ShowBulkDeleteConfirmation => _appState.Config.IsProduction && IsSelectionMode && _selectedKeys.Count > 0;

    public Visibility BulkDeleteConfirmationVisibility => ShowBulkDeleteConfirmation
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanToggleSelectionMode => IsConnected && _keys.Count > 0 && !IsWorking;

    public bool CanSelectAllLoaded => IsSelectionMode && _keys.Count > 0 && !IsWorking;

    public bool CanClearSelection => IsSelectionMode && _selectedKeys.Count > 0 && !IsWorking;

    public bool CanExportLoadedKeys => IsConnected && _keys.Count > 0 && !IsWorking;

    public bool CanDeleteSelectedKeys => IsSelectionMode
        && _selectedKeys.Count > 0
        && !IsWorking
        && (!_appState.Config.IsProduction || string.Equals(BulkDeleteConfirmText, "CONFIRM", StringComparison.Ordinal));

    public string DeleteSelectedKeysLabel => _selectedKeys.Count == 1
        ? "Delete 1 selected key"
        : $"Delete {_selectedKeys.Count} selected keys";

    [RelayCommand]
    private async Task AnalyzeKeyspaceHealthAsync()
    {
        if (_client is null || _keys.Count == 0)
        {
            return;
        }

        IsAnalyzingHealth = true;
        ErrorMessage = null;
        RefreshAnalyticsState();

        try
        {
            var cancellationToken = _loadCts.Token;
            var keyInfos = await EnsureScannedKeyInfosAsync(cancellationToken, forceReload: true);
            var estimatedKeyCount = await TryGetEstimatedKeyCountAsync(cancellationToken);

            _healthReport = _healthAnalyzer.Analyze(
                keyInfos,
                estimatedKeyCount,
                new RedisHealthScanOptions
                {
                    Separator = EffectiveSeparator,
                });

            HealthCoverageText = BuildHealthCoverageText(_healthReport);
            HealthSignalText = BuildHealthSignalText(_healthReport);
            ApplyHealthFilter();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureActionError("Analyzing Redis keyspace health failed.", ex);
        }
        finally
        {
            IsAnalyzingHealth = false;
            RefreshAnalyticsState();
        }
    }

    [RelayCommand]
    private async Task AnalyzePrefixMemoryAsync()
    {
        if (_client is null || _keys.Count == 0)
        {
            return;
        }

        IsAnalyzingMemory = true;
        ErrorMessage = null;
        RefreshAnalyticsState();

        try
        {
            var keyInfos = await EnsureScannedKeyInfosAsync(_loadCts.Token, forceReload: true);
            PrefixMemoryBuckets.Clear();

            foreach (var bucket in RedisKeyGrouper.ComputePrefixMemory(keyInfos, EffectiveSeparator))
            {
                PrefixMemoryBuckets.Add(new PrefixMemoryBucketItemViewModel(bucket));
            }

            PrefixMemorySummaryText = PrefixMemoryBuckets.Count == 0
                ? "No prefix memory buckets were produced from the currently loaded keys."
                : $"Sampled {_scannedKeyInfos.Count} of {_keys.Count} loaded keys across {PrefixMemoryBuckets.Count} prefix bucket{(PrefixMemoryBuckets.Count == 1 ? string.Empty : "s")}.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureActionError("Analyzing Redis memory by prefix failed.", ex);
        }
        finally
        {
            IsAnalyzingMemory = false;
            RefreshAnalyticsState();
        }
    }

    [RelayCommand]
    private async Task LoadSlowLogAsync()
    {
        if (_client is null)
        {
            return;
        }

        await ResetInsightsTokenAsync();
        IsLoadingSlowLog = true;
        ErrorMessage = null;
        SlowLogEntries.Clear();
        HotKeySignals.Clear();
        SlowLogSummaryText = "Loading slowlog...";
        RefreshAnalyticsState();

        try
        {
            var cancellationToken = _insightsCts.Token;
            var slowLog = await _client.GetSlowLogAsync(ct: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var keyInfos = await EnsureScannedKeyInfosAsync(cancellationToken, forceReload: true);
            var hotKeySummary = _opsInsightsAggregator.BuildHotKeySignals(slowLog, keyInfos);
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var entry in slowLog.Entries)
            {
                SlowLogEntries.Add(new RedisSlowLogEntryItemViewModel(entry));
            }

            foreach (var signal in hotKeySummary.Signals)
            {
                HotKeySignals.Add(new RedisHotKeySignalItemViewModel(signal));
            }

            SlowLogSummaryText = BuildSlowLogSummaryText(slowLog);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureActionError("Loading Redis slowlog failed.", ex);
        }
        finally
        {
            IsLoadingSlowLog = false;
            RefreshAnalyticsState();
        }
    }

    [RelayCommand]
    private async Task LoadPubSubAsync()
    {
        if (_client is null)
        {
            return;
        }

        await ResetInsightsTokenAsync();
        IsLoadingPubSub = true;
        ErrorMessage = null;
        PubSubChannels.Clear();
        PubSubSummaryText = "Loading Pub/Sub snapshot...";
        RefreshAnalyticsState();

        try
        {
            var cancellationToken = _insightsCts.Token;
            var snapshot = await _client.GetPubSubSnapshotAsync(ct: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var channel in snapshot.Channels)
            {
                PubSubChannels.Add(new RedisPubSubChannelItemViewModel(channel));
            }

            PubSubSummaryText = BuildPubSubSummaryText(snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureActionError("Loading Redis Pub/Sub snapshot failed.", ex);
        }
        finally
        {
            IsLoadingPubSub = false;
            RefreshAnalyticsState();
        }
    }

    [RelayCommand]
    private void ToggleSelectionMode()
    {
        IsSelectionMode = !IsSelectionMode;
        if (!IsSelectionMode)
        {
            _selectedKeys.Clear();
            BulkDeleteConfirmText = string.Empty;
        }

        RebuildTree();
        RefreshSelectionState();
    }

    [RelayCommand]
    private void SelectAllLoaded()
    {
        _selectedKeys.Clear();
        foreach (var key in _keys)
        {
            _selectedKeys.Add(key);
        }

        IsSelectionMode = _keys.Count > 0;
        BulkDeleteConfirmText = string.Empty;
        RebuildTree();
        RefreshSelectionState();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        _selectedKeys.Clear();
        BulkDeleteConfirmText = string.Empty;
        RebuildTree();
        RefreshSelectionState();
    }

    [RelayCommand]
    private void ToggleTreeSelection(RedisTreeRowViewModel? row)
    {
        if (row is null || row.SelectionKeys.Count == 0)
        {
            return;
        }

        IsSelectionMode = true;

        if (row.IsFullySelected)
        {
            foreach (var key in row.SelectionKeys)
            {
                _selectedKeys.Remove(key);
            }
        }
        else
        {
            foreach (var key in row.SelectionKeys)
            {
                _selectedKeys.Add(key);
            }
        }

        BulkDeleteConfirmText = string.Empty;
        RebuildTree();
        RefreshSelectionState();
    }

    [RelayCommand]
    private async Task DeleteSelectedKeysAsync()
    {
        if (_client is null || _selectedKeys.Count == 0)
        {
            return;
        }

        if (_appState.Config.IsProduction && !string.Equals(BulkDeleteConfirmText, "CONFIRM", StringComparison.Ordinal))
        {
            ErrorMessage = "Type CONFIRM before deleting selected production keys.";
            RefreshSelectionState();
            return;
        }

        var keysToDelete = _selectedKeys.ToList();
        IsDeletingSelectedKeys = true;
        ErrorMessage = null;

        try
        {
            foreach (var chunk in keysToDelete.Chunk(10))
            {
                await _client.DeleteKeysAsync(chunk, _loadCts.Token);
            }

            foreach (var key in keysToDelete)
            {
                _keys.Remove(key);
                _keyTypes.Remove(key);
            }

            if (SelectedKey is not null && keysToDelete.Contains(SelectedKey, StringComparer.Ordinal))
            {
                SelectedKey = null;
                ClearDetailCollections();
            }

            _selectedKeys.Clear();
            IsSelectionMode = false;
            BulkDeleteConfirmText = string.Empty;
            InvalidateAnalysisState();
            RebuildTree();
            UpdateScanSummary();
            _notifications.ShowSuccess("Keys deleted", keysToDelete.Count == 1 ? keysToDelete[0] : $"{keysToDelete.Count} keys removed.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureActionError("Deleting selected Redis keys failed.", ex);
        }
        finally
        {
            IsDeletingSelectedKeys = false;
            RefreshSelectionState();
        }
    }

    [RelayCommand]
    private async Task ExportLoadedKeysAsync()
    {
        if (_client is null || _keys.Count == 0)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        RefreshAllState();

        try
        {
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var key in _keys)
            {
                try
                {
                    var keyInfo = await _client.GetKeyInfoAsync(key, _loadCts.Token);
                    payload[key] = keyInfo.Type switch
                    {
                        "string" => await _client.GetKeyValueAsync(key, _loadCts.Token),
                        "hash" => (await _client.GetHashFieldsAsync(key, _loadCts.Token)).ToDictionary(field => field.Field, field => field.Value, StringComparer.Ordinal),
                        "list" => await _client.GetListItemsAsync(key, 0, -1, _loadCts.Token),
                        "set" => await _client.GetSetMembersAsync(key, _loadCts.Token),
                        "zset" => (await _client.GetSortedSetMembersAsync(key, 0, -1, _loadCts.Token))
                            .Select(entry => new { entry.Member, entry.Score })
                            .ToList(),
                        _ => null,
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    payload[key] = "<error reading key>";
                }
            }

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            var destinationPath = BuildExportPath($"redis-export-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(destinationPath, json, _loadCts.Token);
            _notifications.ShowSuccess("Export complete", destinationPath);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureActionError("Exporting Redis keys to JSON failed.", ex);
        }
        finally
        {
            IsLoading = false;
            RefreshAllState();
        }
    }

    [RelayCommand]
    private void ShowAllHealthFindings()
    {
        HealthSeverityFilter = null;
        ApplyHealthFilter();
    }

    [RelayCommand]
    private void FilterCriticalHealthFindings()
    {
        HealthSeverityFilter = HealthSeverityFilter == RedisHealthSeverity.Critical ? null : RedisHealthSeverity.Critical;
        ApplyHealthFilter();
    }

    [RelayCommand]
    private void FilterWarningHealthFindings()
    {
        HealthSeverityFilter = HealthSeverityFilter == RedisHealthSeverity.Warning ? null : RedisHealthSeverity.Warning;
        ApplyHealthFilter();
    }

    [RelayCommand]
    private void FilterInfoHealthFindings()
    {
        HealthSeverityFilter = HealthSeverityFilter == RedisHealthSeverity.Info ? null : RedisHealthSeverity.Info;
        ApplyHealthFilter();
    }

    [RelayCommand]
    private async Task OpenHealthFindingAsync(RedisHealthFindingItemViewModel? finding)
    {
        if (finding?.Finding.DrillKey is null)
        {
            return;
        }

        await SelectHealthRelatedKeyAsync(finding.Finding.DrillKey);
    }

    [RelayCommand]
    private async Task OpenHotKeySignalAsync(RedisHotKeySignalItemViewModel? signal)
    {
        if (signal is null)
        {
            return;
        }

        await SelectHealthRelatedKeyAsync(signal.Signal.Key);
    }

    private async Task SelectHealthRelatedKeyAsync(string key)
    {
        if (!_keys.Contains(key, StringComparer.Ordinal))
        {
            _notifications.ShowWarning("Key not loaded", key);
            return;
        }

        SelectedKey = key;
        await RefreshSelectedKeyAsync();
    }

    private async Task<IReadOnlyList<RedisKeyInfo>> EnsureScannedKeyInfosAsync(CancellationToken cancellationToken, bool forceReload)
    {
        if (!forceReload && _scannedKeyInfos.Count == _keys.Count && _scannedKeyInfos.Count > 0)
        {
            return _scannedKeyInfos;
        }

        _scannedKeyInfos.Clear();

        if (_client is null)
        {
            return _scannedKeyInfos;
        }

        foreach (var key in _keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _scannedKeyInfos.Add(await _client.GetKeyInfoAsync(key, cancellationToken));
        }

        return _scannedKeyInfos;
    }

    private async Task<long?> TryGetEstimatedKeyCountAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            return null;
        }

        try
        {
            var serverInfo = await _client.GetServerInfoAsync(cancellationToken);
            var databaseIndex = Math.Clamp(SelectedCache?.Database ?? 0, 0, 15);
            return serverInfo.Databases.FirstOrDefault(database => database.Index == databaseIndex)?.Keys;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task ResetInsightsTokenAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        if (!_insightsCts.IsCancellationRequested)
        {
            await CancelTokenAsync(_insightsCts);
        }

        if (_isDisposed)
        {
            return;
        }

        _insightsCts.Dispose();
        _insightsCts = new CancellationTokenSource();
    }

    private void InvalidateAnalysisState()
    {
        _healthReport = null;
        HealthSeverityFilter = null;
        HealthCoverageText = "Run Analyze to inspect loaded keys for TTL, size, prefix, and hot-key risks.";
        HealthSignalText = string.Empty;
        PrefixMemorySummaryText = "Run Analyze to inspect memory distribution by prefix.";
        SlowLogSummaryText = "Load slowlog to inspect expensive Redis commands and inferred hot keys.";
        PubSubSummaryText = "Load Pub/Sub to inspect active channels and subscription pressure.";
        _scannedKeyInfos.Clear();
        HealthFindings.Clear();
        PrefixMemoryBuckets.Clear();
        SlowLogEntries.Clear();
        HotKeySignals.Clear();
        PubSubChannels.Clear();
        RefreshAnalyticsState();
    }

    private void ResetSelectionState(bool clearSelectionMode)
    {
        _selectedKeys.Clear();
        BulkDeleteConfirmText = string.Empty;
        if (clearSelectionMode)
        {
            IsSelectionMode = false;
        }

        RefreshSelectionState();
    }

    private void ApplyHealthFilter()
    {
        HealthFindings.Clear();

        if (_healthReport is null)
        {
            RefreshAnalyticsState();
            return;
        }

        foreach (var finding in _healthReport.Findings.Where(finding => HealthSeverityFilter is null || finding.Severity == HealthSeverityFilter.Value))
        {
            HealthFindings.Add(new RedisHealthFindingItemViewModel(finding));
        }

        RefreshAnalyticsState();
    }

    private IReadOnlyList<string> CollectSelectionKeys(NamespaceNode node)
    {
        if (node.IsKey)
        {
            return node.FullKey is null ? Array.Empty<string>() : [node.FullKey];
        }

        var keys = new List<string>();
        AppendSelectionKeys(node, keys);
        return keys;
    }

    private static void AppendSelectionKeys(NamespaceNode node, List<string> keys)
    {
        if (node.IsKey)
        {
            if (!string.IsNullOrWhiteSpace(node.FullKey))
            {
                keys.Add(node.FullKey);
            }

            return;
        }

        foreach (var child in node.Children)
        {
            AppendSelectionKeys(child, keys);
        }
    }

    private static string BuildHealthCoverageText(RedisKeyspaceHealthReport report)
    {
        if (report.EstimatedKeyCount.HasValue)
        {
            return $"Coverage: {report.LoadedKeyCount} loaded of {report.EstimatedKeyCount.Value} estimated keys ({report.CoveragePercent:0.#}%).";
        }

        return $"Coverage: {report.LoadedKeyCount} loaded keys (estimated total unavailable).";
    }

    private static string BuildHealthSignalText(RedisKeyspaceHealthReport report)
    {
        if (!report.HotKeySignalsAvailable)
        {
            return "Hot-key signals unavailable for this scan; only size, TTL, and prefix heuristics are applied.";
        }

        return $"Hot-key signal coverage: {report.KeysWithHotKeySignal} of {report.LoadedKeyCount} keys expose OBJECT FREQ or IDLETIME.";
    }

    private static string BuildSlowLogSummaryText(RedisSlowLogSummary summary)
    {
        var capability = summary.Capability == RedisInsightCapability.Loaded
            ? string.Empty
            : $" Capability: {summary.Capability}.";
        var truncated = summary.Truncated ? $" Showing the most recent {summary.MaxReturned} entries." : string.Empty;
        return $"Loaded {summary.Entries.Count} slowlog entr{(summary.Entries.Count == 1 ? "y" : "ies")}.{truncated}{capability}".Trim();
    }

    private static string BuildPubSubSummaryText(RedisPubSubSnapshot snapshot)
    {
        var truncated = snapshot.Truncated ? $" Showing the first {snapshot.MaxChannels} channel(s)." : string.Empty;
        return $"Loaded {snapshot.Channels.Count} channel(s) and {snapshot.PatternSubscriptionCount} pattern subscription(s).{truncated}";
    }

    private static string BuildExportPath(string fileName)
    {
        var downloadsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        Directory.CreateDirectory(downloadsFolder);

        var sanitizedFileName = SanitizeFileName(fileName);
        var candidatePath = Path.Combine(downloadsFolder, sanitizedFileName);
        if (!File.Exists(candidatePath))
        {
            return candidatePath;
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sanitizedFileName);
        var extension = Path.GetExtension(sanitizedFileName);

        for (var index = 1; index < 1000; index++)
        {
            candidatePath = Path.Combine(downloadsFolder, $"{fileNameWithoutExtension} ({index}){extension}");
            if (!File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return Path.Combine(downloadsFolder, $"{Guid.NewGuid():N}{extension}");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(fileName.Select(character => invalidChars.Contains(character) ? '_' : character));
    }

    private void RefreshAnalyticsState()
    {
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(HasHealthReport));
        OnPropertyChanged(nameof(ShowHealthEmptyState));
        OnPropertyChanged(nameof(ShowFilteredHealthEmptyState));
        OnPropertyChanged(nameof(HealthFiltersVisibility));
        OnPropertyChanged(nameof(ShowPrefixMemoryEmptyState));
        OnPropertyChanged(nameof(ShowSlowLogEmptyState));
        OnPropertyChanged(nameof(ShowHotKeySignalsEmptyState));
        OnPropertyChanged(nameof(ShowPubSubEmptyState));
        OnPropertyChanged(nameof(CanAnalyzeHealth));
        OnPropertyChanged(nameof(CanAnalyzePrefixMemory));
        OnPropertyChanged(nameof(CanLoadSlowLog));
        OnPropertyChanged(nameof(CanLoadPubSub));
        OnPropertyChanged(nameof(HealthAllFilterLabel));
        OnPropertyChanged(nameof(HealthCriticalFilterLabel));
        OnPropertyChanged(nameof(HealthWarningFilterLabel));
        OnPropertyChanged(nameof(HealthInfoFilterLabel));
        OnPropertyChanged(nameof(IsHealthAllFilterActive));
        OnPropertyChanged(nameof(IsHealthCriticalFilterActive));
        OnPropertyChanged(nameof(IsHealthWarningFilterActive));
        OnPropertyChanged(nameof(IsHealthInfoFilterActive));
    }

    private void RefreshSelectionState()
    {
        OnPropertyChanged(nameof(CanToggleSelectionMode));
        OnPropertyChanged(nameof(CanSelectAllLoaded));
        OnPropertyChanged(nameof(CanClearSelection));
        OnPropertyChanged(nameof(CanExportLoadedKeys));
        OnPropertyChanged(nameof(CanDeleteSelectedKeys));
        OnPropertyChanged(nameof(HeaderBulkToolbarVisibility));
        OnPropertyChanged(nameof(HeaderMessagesVisibility));
        OnPropertyChanged(nameof(SelectionModeButtonLabel));
        OnPropertyChanged(nameof(SelectionSummaryText));
        OnPropertyChanged(nameof(SelectionSummaryVisibility));
        OnPropertyChanged(nameof(ShowBulkDeleteConfirmation));
        OnPropertyChanged(nameof(BulkDeleteConfirmationVisibility));
        OnPropertyChanged(nameof(DeleteSelectedKeysLabel));
        OnPropertyChanged(nameof(SelectedKeyCount));
    }

    partial void OnIsAnalyzingHealthChanged(bool value) => RefreshAnalyticsState();

    partial void OnIsAnalyzingMemoryChanged(bool value) => RefreshAnalyticsState();

    partial void OnIsLoadingSlowLogChanged(bool value) => RefreshAnalyticsState();

    partial void OnIsLoadingPubSubChanged(bool value) => RefreshAnalyticsState();

    partial void OnIsDeletingSelectedKeysChanged(bool value) => RefreshAllState();

    partial void OnIsSelectionModeChanged(bool value) => RefreshSelectionState();

    partial void OnBulkDeleteConfirmTextChanged(string value) => RefreshSelectionState();

    partial void OnHealthSeverityFilterChanged(RedisHealthSeverity? value) => RefreshAnalyticsState();
}