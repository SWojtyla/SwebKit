using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.Redis;
using SwebKit.WinUI.Services;

namespace SwebKit.WinUI.ViewModels.Redis;

public sealed partial class RedisPageViewModel : ObservableObject, IAsyncDisposable
{
    private const int ScanPageSize = 250;
    private const int ScanBatchSize = 250;
    private const int KeyTypeBatchSize = 25;
    private const int ItemPageSize = 100;
    private const string DemoCacheId = "demo-cache";
    private const string DemoCacheDisplayName = "Demo cache";
    private const int TtlServerRefreshTickBudget = 30;
    private static readonly TimeSpan TtlCountdownInterval = TimeSpan.FromSeconds(1);

    private readonly AppStateService _appState;
    private readonly IRedisClientFactory _redisClientFactory;
    private readonly INotificationService _notifications;
    private readonly OperatorWorkspaceService _workspaceService;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly ILogger<RedisPageViewModel> _logger;
    private readonly RedisScanPageAccumulator _scanAccumulator = new(ScanPageSize);
    private readonly List<string> _keys = [];
    private readonly Dictionary<string, string> _keyTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expandedPrefixes = new(StringComparer.Ordinal);
    private CancellationTokenSource _loadCts = new();
    private CancellationTokenSource _detailCts = new();
    private IRedisClient? _client;
    private long _scanCursor;
    private bool _hasMoreKeys;
    private long _listOffset;
    private long _setCursor;
    private bool _hasMoreItems;
    private bool _loaded;
    private bool _isDisposed;
    private bool _suppressSelectionSideEffects;
    private CancellationTokenSource _ttlCountdownCts = new();
    private string? _ttlTrackedKey;
    private TimeSpan? _ttlOriginal;
    private TimeSpan? _ttlDisplayed;

    public RedisPageViewModel(
        AppStateService appState,
        IRedisClientFactory redisClientFactory,
        RedisOpsInsightsAggregator opsInsightsAggregator,
        INotificationService notifications,
        OperatorWorkspaceService workspaceService,
        ILogger<RedisPageViewModel> logger)
    {
        _appState = appState;
        _redisClientFactory = redisClientFactory;
        _opsInsightsAggregator = opsInsightsAggregator;
        _notifications = notifications;
        _workspaceService = workspaceService;
        _logger = logger;
        try
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        }
        catch (COMException)
        {
            _dispatcherQueue = null;
        }
        _appState.DemoModeChanged += OnDemoModeChanged;

        CacheEntries.CollectionChanged += (_, _) => RefreshConnectionState();
        TreeRows.CollectionChanged += (_, _) => RefreshBrowserState();
        HashFields.CollectionChanged += (_, _) => RefreshDetailState();
        CollectionItems.CollectionChanged += (_, _) => RefreshDetailState();
        SortedSetEntries.CollectionChanged += (_, _) => RefreshDetailState();
        _workspaceService.RegisterRestoreHandler("redis", RestoreWorkspaceAsync);
    }

    public ObservableCollection<RedisCacheEntry> CacheEntries { get; } = [];

    public ObservableCollection<RedisTreeRowViewModel> TreeRows { get; } = [];

    public ObservableCollection<RedisHashFieldItemViewModel> HashFields { get; } = [];

    public ObservableCollection<string> CollectionItems { get; } = [];

    public ObservableCollection<RedisSortedSetEntryItemViewModel> SortedSetEntries { get; } = [];

    [ObservableProperty]
    public partial RedisCacheEntry? SelectedCache { get; set; }

    [ObservableProperty]
    public partial string PatternInput { get; set; } = "*";

    [ObservableProperty]
    public partial string SeparatorInput { get; set; } = "-";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsDetailLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string ConnectionSummary { get; set; } = "Redis not connected.";

    [ObservableProperty]
    public partial string ScanSummary { get; set; } = "No keys loaded yet.";

    [ObservableProperty]
    public partial string? SelectedKey { get; set; }

    [ObservableProperty]
    public partial RedisKeyInfo? SelectedKeyInfo { get; set; }

    [ObservableProperty]
    public partial string SelectedStringEditorValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSelectedStringBinary { get; set; }

    [ObservableProperty]
    public partial string RenameInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double TtlEditorSeconds { get; set; } = 300;

    [ObservableProperty]
    public partial string HashFieldEditorName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HashFieldEditorValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SortedSetMemberEditor { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double SortedSetScoreEditor { get; set; }

    [ObservableProperty]
    public partial bool IsDeleteConfirmationArmed { get; set; }

    public bool IsConfigured => _appState.UseDemoData || CacheEntries.Any(entry => !string.IsNullOrWhiteSpace(entry.ConnectionString));

    public bool HasConfiguredCaches => CacheEntries.Count > 0;

    public bool IsConnected => _client is not null;

    public bool IsWorking => IsLoading || IsDetailLoading || IsAnalyzingHealth || IsAnalyzingMemory || IsLoadingSlowLog || IsLoadingPubSub || IsDeletingSelectedKeys;

    public bool ShowNotConfiguredState => !IsLoading && !IsConfigured;

    public bool ShowTreeEmptyState => IsConfigured && !IsLoading && TreeRows.Count == 0 && string.IsNullOrWhiteSpace(ErrorMessage);

    public bool ShowSelectionEmptyState => !IsDetailLoading && SelectedKey is null;

    public Visibility DetailLoadingVisibility => SelectedKey is not null && IsDetailLoading
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility DetailContentVisibility => SelectedKeyInfo is not null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility StringSectionVisibility => SelectedKeyInfo?.Type == "string"
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility StringEditorVisibility => SelectedKeyInfo?.Type == "string" && !IsSelectedStringBinary
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility HashSectionVisibility => SelectedKeyInfo?.Type == "hash"
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility CollectionSectionVisibility => SelectedKeyInfo?.Type is "list" or "set"
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility SortedSetSectionVisibility => SelectedKeyInfo?.Type == "zset"
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ErrorVisibility => string.IsNullOrWhiteSpace(ErrorMessage)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility DeleteSelectedCancelVisibility => IsDeleteConfirmationArmed
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool ShowHashEmptyState => SelectedKeyInfo?.Type == "hash" && HashFields.Count == 0;

    public string LoadMoreKeysLabel => _hasMoreKeys
        ? $"Load more matches ({_keys.Count} loaded)"
        : "No more matches";

    public Visibility HeaderStatusRowVisibility => IsConfigured ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HeaderMessagesVisibility => ShowBulkDeleteConfirmation || !string.IsNullOrWhiteSpace(ErrorMessage) || ShowNotConfiguredState
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility LoadMoreKeysVisibility => _hasMoreKeys ? Visibility.Visible : Visibility.Collapsed;

    public bool CanReload => !IsWorking;

    public bool CanLoadMoreKeys => _hasMoreKeys && IsConnected && !IsWorking;

    public bool CanChangeCache => HasConfiguredCaches && !IsWorking;

    public bool CanEditPatternInput => !IsWorking;

    public bool CanEditSeparatorInput => !IsWorking;

    public string DetailTitle => SelectedKey ?? "Key detail";

    public string DetailStatusText => BuildDetailStatus();

    public string SelectedTypeText => SelectedKeyInfo?.Type ?? "Unavailable";

    public string SelectedTtlText => TtlFormatter.FormatHuman(_ttlDisplayed);

    public double SelectedTtlProgressValue => TtlFormatter.GetBarWidthPercent(_ttlDisplayed, _ttlOriginal);

    public Visibility SelectedTtlProgressVisibility => _ttlDisplayed is { } ttl && ttl > TimeSpan.Zero
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility SelectedTtlHealthyProgressVisibility => SelectedTtlProgressVisibility == Visibility.Visible
        && GetSelectedTtlVisualState() == TtlVisualState.Success
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility SelectedTtlWarningProgressVisibility => SelectedTtlProgressVisibility == Visibility.Visible
        && GetSelectedTtlVisualState() == TtlVisualState.Warning
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility SelectedTtlCriticalProgressVisibility => SelectedTtlProgressVisibility == Visibility.Visible
        && GetSelectedTtlVisualState() == TtlVisualState.Critical
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string SelectedMemoryText => FormatBytes(SelectedKeyInfo?.MemoryBytes);

    public string SelectedEncodingText => string.IsNullOrWhiteSpace(SelectedKeyInfo?.Encoding)
        ? "Unavailable"
        : SelectedKeyInfo.Encoding!;

    public string SelectedFrequencyText => SelectedKeyInfo?.Frequency?.ToString() ?? "Unavailable";

    public string SelectedIdleText => SelectedKeyInfo?.IdleSeconds is long seconds
        ? FormatDuration(TimeSpan.FromSeconds(seconds))
        : "Unavailable";

    public string CollectionSectionTitle => SelectedKeyInfo?.Type == "list" ? "List items" : "Set members";

    public string CollectionSectionStatus => SelectedKeyInfo?.Type switch
    {
        "list" => BuildCollectionStatus("list item", CollectionItems.Count),
        "set" => BuildCollectionStatus("set member", CollectionItems.Count),
        _ => "",
    };

    public string SortedSetStatus => BuildCollectionStatus("sorted set member", SortedSetEntries.Count);

    public string LoadMoreItemsLabel => _hasMoreItems ? "Load more items" : "All items loaded";

    public bool CanRefreshSelectedKey => SelectedKey is not null && IsConnected && !IsDetailLoading;

    public bool CanRenameSelectedKey =>
        SelectedKey is not null &&
        IsConnected &&
        !IsDetailLoading &&
        !string.IsNullOrWhiteSpace(RenameInput) &&
        !string.Equals(RenameInput.Trim(), SelectedKey, StringComparison.Ordinal);

    public bool CanDeleteSelectedKey => SelectedKey is not null && IsConnected && !IsDetailLoading;

    public string DeleteSelectedLabel => IsDeleteConfirmationArmed ? "Confirm delete" : "Delete key";

    public bool CanApplyTtl => SelectedKey is not null && IsConnected && !IsDetailLoading && TtlEditorSeconds >= 1;

    public bool CanRemoveTtl => SelectedKey is not null && IsConnected && !IsDetailLoading;

    public bool CanSaveStringValue =>
        SelectedKeyInfo?.Type == "string" &&
        SelectedKey is not null &&
        IsConnected &&
        !IsDetailLoading &&
        !IsSelectedStringBinary;

    public bool CanUpsertHashField =>
        SelectedKeyInfo?.Type == "hash" &&
        SelectedKey is not null &&
        IsConnected &&
        !IsDetailLoading &&
        !string.IsNullOrWhiteSpace(HashFieldEditorName);

    public bool CanApplySortedSetScore =>
        SelectedKeyInfo?.Type == "zset" &&
        SelectedKey is not null &&
        IsConnected &&
        !IsDetailLoading &&
        !string.IsNullOrWhiteSpace(SortedSetMemberEditor);

    public bool CanLoadMoreItems =>
        _hasMoreItems &&
        SelectedKeyInfo?.Type is "list" or "set" or "zset" &&
        !IsDetailLoading;

    public async Task LoadAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await ReloadAsync();
        await _workspaceService.ApplyPendingRestoreAsync("redis");
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await _appState.WhenInitializedAsync();
        SyncConfigState();
        ErrorMessage = null;
        await ResetInsightsTokenAsync();

        if (!IsConfigured)
        {
            await ResetLoadTokenAsync();
            await ResetDetailTokenAsync();
            DisposeClient();
            ResetKeyBrowser(clearSelection: true);
            ConnectionSummary = "No Redis cache is configured. Add a cache connection in Settings before opening this workspace.";
            _workspaceService.ClearCurrentSnapshot("redis");
            RefreshAllState();
            return;
        }

        await ResetLoadTokenAsync();
        var cancellationToken = _loadCts.Token;
        IsLoading = true;
        RefreshConnectionState();

        try
        {
            await ConnectAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (_client is null)
            {
                await PublishSnapshotSafeAsync(recordRecent: false);
                return;
            }

            await ScanAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureLoadError("Redis reload failed.", ex);
        }
        finally
        {
            IsLoading = false;
            RefreshConnectionState();
        }
    }

    [RelayCommand]
    private void ToggleTreeRow(RedisTreeRowViewModel? row)
    {
        if (row is null || row.IsKey)
        {
            return;
        }

        if (!_expandedPrefixes.Add(row.Prefix))
        {
            _expandedPrefixes.Remove(row.Prefix);
        }

        RebuildTree();
    }

    [RelayCommand]
    private async Task SelectTreeKeyAsync(RedisTreeRowViewModel? row)
    {
        if (row?.FullKey is null)
        {
            return;
        }

        if (string.Equals(SelectedKey, row.FullKey, StringComparison.Ordinal) && SelectedKeyInfo is not null)
        {
            await RefreshSelectedKeyAsync();
            return;
        }

        SelectedKey = row.FullKey;
        await RefreshSelectedKeyAsync();
    }

    [RelayCommand]
    private async Task FilterByPrefixAsync(RedisTreeRowViewModel? row)
    {
        if (row is null || row.IsKey)
        {
            return;
        }

        PatternInput = $"{row.Prefix}{EffectiveSeparator}*";
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task LoadMoreKeysAsync()
    {
        if (_client is null || !_hasMoreKeys || IsLoading)
        {
            return;
        }

        var cancellationToken = _loadCts.Token;
        IsLoading = true;
        ErrorMessage = null;
        RefreshConnectionState();

        try
        {
            var nextPage = _scanAccumulator.TakeOverflowPage(0).ToList();
            var pattern = EffectivePattern;

            while (nextPage.Count < ScanPageSize && _scanCursor != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await _client.ScanKeysAsync(pattern, _scanCursor, ScanBatchSize, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                _scanCursor = result.Cursor;
                var appended = _scanAccumulator.AppendBatch(result.Keys, nextPage.Count);
                nextPage.AddRange(appended.VisibleKeys);
                _hasMoreKeys = _scanAccumulator.HasOverflow || (!result.IsComplete && _scanCursor != 0);

                if (_scanAccumulator.HasOverflow || nextPage.Count >= ScanPageSize || result.IsComplete || _scanCursor == 0)
                {
                    break;
                }
            }

            if (nextPage.Count == 0)
            {
                _hasMoreKeys = _scanAccumulator.HasOverflow || _scanCursor != 0;
                UpdateScanSummary();
                return;
            }

            foreach (var key in nextPage)
            {
                _keys.Add(key);
            }

            InvalidateAnalysisState();
            RebuildTree();
            UpdateScanSummary();
            await LoadVisibleKeyTypesAsync(nextPage, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureLoadError("Loading more Redis keys failed.", ex);
        }
        finally
        {
            IsLoading = false;
            RefreshConnectionState();
        }
    }

    [RelayCommand]
    private async Task RefreshSelectedKeyAsync()
    {
        if (_client is null || string.IsNullOrWhiteSpace(SelectedKey))
        {
            return;
        }

        await ResetDetailTokenAsync();
        var cancellationToken = _detailCts.Token;
        IsDetailLoading = true;
        ErrorMessage = null;
        ClearDetailCollections();
        RefreshDetailState();

        try
        {
            var key = SelectedKey;
            var info = await _client.GetKeyInfoAsync(key, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            SelectedKeyInfo = info;
            _keyTypes[key] = info.Type;
            RenameInput = key;
            TtlEditorSeconds = info.Ttl is { } ttl && ttl > TimeSpan.Zero
                ? Math.Ceiling(ttl.TotalSeconds)
                : 300;

            switch (info.Type)
            {
                case "string":
                    var stringValue = await _client.GetKeyValueAsync(key, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    IsSelectedStringBinary = RedisValueHelpers.IsBinaryContent(stringValue);
                    SelectedStringEditorValue = IsSelectedStringBinary ? string.Empty : stringValue ?? string.Empty;
                    break;

                case "hash":
                    foreach (var field in await _client.GetHashFieldsAsync(key, cancellationToken))
                    {
                        HashFields.Add(new RedisHashFieldItemViewModel(field));
                    }

                    break;

                case "list":
                    var listItems = await _client.GetListItemsAsync(key, 0, ItemPageSize - 1, cancellationToken);
                    foreach (var item in listItems)
                    {
                        CollectionItems.Add(item);
                    }

                    _listOffset = CollectionItems.Count;
                    _hasMoreItems = listItems.Count >= ItemPageSize;
                    break;

                case "set":
                    var setPage = await _client.GetSetMembersPageAsync(key, 0, ItemPageSize, cancellationToken);
                    foreach (var item in setPage.Members)
                    {
                        CollectionItems.Add(item);
                    }

                    _setCursor = setPage.Cursor;
                    _hasMoreItems = !setPage.IsComplete;
                    break;

                case "zset":
                    var sortedSetEntries = await _client.GetSortedSetMembersAsync(key, 0, ItemPageSize - 1, cancellationToken);
                    foreach (var entry in sortedSetEntries)
                    {
                        SortedSetEntries.Add(new RedisSortedSetEntryItemViewModel(entry));
                    }

                    _listOffset = SortedSetEntries.Count;
                    _hasMoreItems = sortedSetEntries.Count >= ItemPageSize;
                    break;
            }

            RebuildTree();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureLoadError("Loading Redis key detail failed.", ex);
        }
        finally
        {
            IsDetailLoading = false;
            RefreshDetailState();
        }
    }

    [RelayCommand]
    private async Task RenameSelectedKeyAsync()
    {
        if (_client is null || string.IsNullOrWhiteSpace(SelectedKey))
        {
            return;
        }

        var oldKey = SelectedKey;
        var newKey = RenameInput.Trim();
        if (string.IsNullOrWhiteSpace(newKey) || string.Equals(oldKey, newKey, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await _client.RenameKeyAsync(oldKey, newKey, _detailCts.Token);

            var index = _keys.IndexOf(oldKey);
            if (index >= 0)
            {
                _keys[index] = newKey;
            }

            if (_keyTypes.Remove(oldKey, out var keyType))
            {
                _keyTypes[newKey] = keyType;
            }

            _scanAccumulator.RegisterVisibleKey(newKey);
            InvalidateAnalysisState();
            SelectedKey = newKey;
            RebuildTree();
            await RefreshSelectedKeyAsync();
            _notifications.ShowSuccess("Key renamed", $"'{oldKey}' -> '{newKey}'");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureActionError("Renaming the selected key failed.", ex);
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedKeyAsync()
    {
        if (_client is null || string.IsNullOrWhiteSpace(SelectedKey))
        {
            return;
        }

        if (!IsDeleteConfirmationArmed)
        {
            IsDeleteConfirmationArmed = true;
            RefreshDetailState();
            return;
        }

        var deletedKey = SelectedKey;

        try
        {
            await _client.DeleteKeysAsync([deletedKey], _detailCts.Token);
            _keys.Remove(deletedKey);
            _keyTypes.Remove(deletedKey);
            InvalidateAnalysisState();
            IsDeleteConfirmationArmed = false;
            SelectedKey = null;
            RebuildTree();
            UpdateScanSummary();
            _notifications.ShowSuccess("Key deleted", deletedKey);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureActionError("Deleting the selected key failed.", ex);
        }
    }

    [RelayCommand]
    private void CancelDeleteSelected()
    {
        IsDeleteConfirmationArmed = false;
        RefreshDetailState();
    }

    [RelayCommand]
    private async Task ApplyTtlAsync()
    {
        if (_client is null || string.IsNullOrWhiteSpace(SelectedKey))
        {
            return;
        }

        try
        {
            var ttlSeconds = Math.Max(1, (int)Math.Ceiling(TtlEditorSeconds));
            await _client.SetTtlAsync(SelectedKey, TimeSpan.FromSeconds(ttlSeconds), _detailCts.Token);
            InvalidateAnalysisState();
            await RefreshSelectedKeyAsync();
            _notifications.ShowSuccess("TTL updated", $"{ttlSeconds}s on '{SelectedKey}'");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureActionError("Updating TTL failed.", ex);
        }
    }

    [RelayCommand]
    private async Task RemoveTtlAsync()
    {
        if (_client is null || string.IsNullOrWhiteSpace(SelectedKey))
        {
            return;
        }

        try
        {
            await _client.RemoveTtlAsync(SelectedKey, _detailCts.Token);
            InvalidateAnalysisState();
            await RefreshSelectedKeyAsync();
            _notifications.ShowSuccess("TTL removed", $"Key '{SelectedKey}' will not expire.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureActionError("Removing TTL failed.", ex);
        }
    }

    [RelayCommand]
    private async Task SaveStringValueAsync()
    {
        if (_client is null || string.IsNullOrWhiteSpace(SelectedKey) || SelectedKeyInfo?.Type != "string")
        {
            return;
        }

        try
        {
            await _client.SetKeyValueAsync(SelectedKey, SelectedStringEditorValue, ct: _detailCts.Token);
            InvalidateAnalysisState();
            await RefreshSelectedKeyAsync();
            _notifications.ShowSuccess("Value saved", SelectedKey);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureActionError("Saving the string value failed.", ex);
        }
    }

    [RelayCommand]
    private void BeginEditHashField(RedisHashFieldItemViewModel? field)
    {
        if (field is null)
        {
            return;
        }

        HashFieldEditorName = field.Field;
        HashFieldEditorValue = field.Value;
        RefreshDetailState();
    }

    [RelayCommand]
    private async Task UpsertHashFieldAsync()
    {
        if (_client is null || string.IsNullOrWhiteSpace(SelectedKey) || SelectedKeyInfo?.Type != "hash")
        {
            return;
        }

        var fieldName = HashFieldEditorName.Trim();
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        try
        {
            await _client.SetHashFieldAsync(SelectedKey, fieldName, HashFieldEditorValue, _detailCts.Token);
            InvalidateAnalysisState();
            await RefreshSelectedKeyAsync();
            _notifications.ShowSuccess("Hash field saved", $"{fieldName} on '{SelectedKey}'");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureActionError("Saving the hash field failed.", ex);
        }
    }

    [RelayCommand]
    private async Task DeleteHashFieldAsync(RedisHashFieldItemViewModel? field)
    {
        if (_client is null || string.IsNullOrWhiteSpace(SelectedKey) || field is null)
        {
            return;
        }

        try
        {
            await _client.DeleteHashFieldAsync(SelectedKey, field.Field, _detailCts.Token);
            InvalidateAnalysisState();
            await RefreshSelectedKeyAsync();
            _notifications.ShowSuccess("Hash field deleted", $"{field.Field} from '{SelectedKey}'");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureActionError("Deleting the hash field failed.", ex);
        }
    }

    [RelayCommand]
    private void BeginEditSortedSetEntry(RedisSortedSetEntryItemViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        SortedSetMemberEditor = entry.Member;
        SortedSetScoreEditor = entry.Score;
        RefreshDetailState();
    }

    [RelayCommand]
    private async Task ApplySortedSetScoreAsync()
    {
        if (_client is null || string.IsNullOrWhiteSpace(SelectedKey) || string.IsNullOrWhiteSpace(SortedSetMemberEditor))
        {
            return;
        }

        try
        {
            await _client.UpdateSortedSetScoreAsync(SelectedKey, SortedSetMemberEditor, SortedSetScoreEditor, _detailCts.Token);
            InvalidateAnalysisState();
            await RefreshSelectedKeyAsync();
            _notifications.ShowSuccess("Sorted set score updated", $"{SortedSetMemberEditor} = {SortedSetScoreEditor:G}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureActionError("Updating the sorted set score failed.", ex);
        }
    }

    [RelayCommand]
    private async Task LoadMoreItemsAsync()
    {
        if (_client is null || string.IsNullOrWhiteSpace(SelectedKey) || !_hasMoreItems || SelectedKeyInfo is null)
        {
            return;
        }

        try
        {
            switch (SelectedKeyInfo.Type)
            {
                case "list":
                    var listPage = await _client.GetListItemsAsync(SelectedKey, _listOffset, _listOffset + ItemPageSize - 1, _detailCts.Token);
                    foreach (var item in listPage)
                    {
                        CollectionItems.Add(item);
                    }

                    _listOffset += listPage.Count;
                    _hasMoreItems = listPage.Count >= ItemPageSize;
                    break;

                case "set":
                    var setPage = await _client.GetSetMembersPageAsync(SelectedKey, _setCursor, ItemPageSize, _detailCts.Token);
                    foreach (var item in setPage.Members)
                    {
                        CollectionItems.Add(item);
                    }

                    _setCursor = setPage.Cursor;
                    _hasMoreItems = !setPage.IsComplete;
                    break;

                case "zset":
                    var sortedSetPage = await _client.GetSortedSetMembersAsync(SelectedKey, _listOffset, _listOffset + ItemPageSize - 1, _detailCts.Token);
                    foreach (var entry in sortedSetPage)
                    {
                        SortedSetEntries.Add(new RedisSortedSetEntryItemViewModel(entry));
                    }

                    _listOffset += sortedSetPage.Count;
                    _hasMoreItems = sortedSetPage.Count >= ItemPageSize;
                    break;
            }

            RefreshDetailState();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CaptureActionError("Loading more items failed.", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _appState.DemoModeChanged -= OnDemoModeChanged;
        _workspaceService.UnregisterRestoreHandler("redis");
        await CancelTokenAsync(_loadCts);
        await CancelTokenAsync(_detailCts);
        await CancelTokenAsync(_insightsCts);
        ResetTtlCountdownToken(recreateToken: false);
        DisposeClient();
        _loadCts.Dispose();
        _detailCts.Dispose();
        _insightsCts.Dispose();
    }

    partial void OnSelectedCacheChanged(RedisCacheEntry? value)
    {
        RefreshConnectionState();

        if (_isDisposed || _suppressSelectionSideEffects || !_loaded)
        {
            return;
        }

        _ = HandleSelectedCacheChangedAsync(value);
    }

    partial void OnSeparatorInputChanged(string value)
    {
        RebuildTree();

        if (_isDisposed || _suppressSelectionSideEffects || !_loaded)
        {
            return;
        }

        _ = PersistSeparatorAsync(NormalizeSeparator(value));
        _ = PublishSnapshotSafeAsync(recordRecent: false);
    }

    partial void OnIsLoadingChanged(bool value)
    {
        RefreshConnectionState();
    }

    partial void OnIsDetailLoadingChanged(bool value)
    {
        RefreshDetailState();
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(ErrorVisibility));
        OnPropertyChanged(nameof(HeaderMessagesVisibility));
        OnPropertyChanged(nameof(ShowTreeEmptyState));
    }

    partial void OnSelectedKeyChanged(string? value)
    {
        IsDeleteConfirmationArmed = false;
        RenameInput = value ?? string.Empty;
        _ttlTrackedKey = value;
        _ttlOriginal = null;
        _ttlDisplayed = null;
        ResetTtlCountdownToken();
        ClearDetailCollections();
        RefreshDetailState();
        RebuildTree();

        if (_isDisposed || _suppressSelectionSideEffects || !_loaded)
        {
            return;
        }

        _ = PublishSnapshotSafeAsync(recordRecent: false);
    }

    partial void OnSelectedKeyInfoChanged(RedisKeyInfo? value)
    {
        SyncSelectedTtlState(value);
        RefreshDetailState();
    }

    partial void OnRenameInputChanged(string value)
    {
        RefreshDetailState();
    }

    partial void OnTtlEditorSecondsChanged(double value)
    {
        RefreshDetailState();
    }

    partial void OnHashFieldEditorNameChanged(string value)
    {
        RefreshDetailState();
    }

    partial void OnSortedSetMemberEditorChanged(string value)
    {
        RefreshDetailState();
    }

    partial void OnIsDeleteConfirmationArmedChanged(bool value)
    {
        RefreshDetailState();
    }

    private RedisConfig? CurrentRedisConfig => _appState.Config.RedisConfig;

    private string EffectivePattern => string.IsNullOrWhiteSpace(PatternInput) ? "*" : PatternInput.Trim();

    private string EffectiveSeparator => NormalizeSeparator(SeparatorInput);

    private void SyncConfigState()
    {
        var redisConfig = CurrentRedisConfig;
        redisConfig?.EnsureMigrated();

        _suppressSelectionSideEffects = true;
        try
        {
            CacheEntries.Clear();
            if (redisConfig is not null)
            {
                foreach (var cacheEntry in redisConfig.Caches)
                {
                    CacheEntries.Add(cacheEntry);
                }
            }

            if (_appState.UseDemoData && CacheEntries.Count == 0)
            {
                CacheEntries.Add(CreateDemoCacheEntry());
            }

            var separator = redisConfig?.NamespaceSeparator;
            if (_appState.UseDemoData && string.IsNullOrWhiteSpace(separator))
            {
                separator = ":";
            }

            SeparatorInput = NormalizeSeparator(separator);
            SelectedCache = redisConfig?.ActiveCache ?? CacheEntries.FirstOrDefault();
        }
        finally
        {
            _suppressSelectionSideEffects = false;
        }

        RefreshConnectionState();
    }

    private void OnDemoModeChanged()
    {
        if (_isDisposed)
        {
            return;
        }

        if (!_loaded)
        {
            RefreshAllState();
            return;
        }

        _ = ReloadAsync();
    }

    private async Task HandleSelectedCacheChangedAsync(RedisCacheEntry? cacheEntry)
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            var redisConfig = CurrentRedisConfig;
            if (redisConfig is not null)
            {
                redisConfig.ActiveCacheId = cacheEntry?.Id;
                await _appState.SaveConfigAsync();
            }

            if (_isDisposed)
            {
                return;
            }

            SelectedKey = null;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            CaptureLoadError("Changing the active Redis cache failed.", ex);
        }
    }

    private async Task PersistSeparatorAsync(string separator)
    {
        try
        {
            if (CurrentRedisConfig is not RedisConfig redisConfig)
            {
                return;
            }

            redisConfig.NamespaceSeparator = separator;
            await _appState.SaveConfigAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Persisting the Redis namespace separator failed.");
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        DisposeClient();
        ConnectionSummary = "Connecting to Redis...";
        RefreshConnectionState();

        if (_appState.UseDemoData)
        {
            var database = SelectedCache?.Database ?? 0;
            _client = new DemoRedisClient(database);
            ConnectionSummary = $"Connected to demo Redis cache on database {database}.";
            RefreshConnectionState();
            return;
        }

        if (SelectedCache is null || string.IsNullOrWhiteSpace(SelectedCache.ConnectionString))
        {
            _client = null;
            ResetKeyBrowser(clearSelection: true);
            ConnectionSummary = "No Redis cache is configured for the current environment.";
            RefreshConnectionState();
            return;
        }

        var client = await _redisClientFactory.CreateAsync(SelectedCache, cancellationToken);
        var isConnected = await client.TestConnectionAsync(cancellationToken);
        if (!isConnected)
        {
            client.Dispose();
            _client = null;
            ResetKeyBrowser(clearSelection: true);
            ErrorMessage = "Redis connection test failed.";
            ConnectionSummary = $"Unable to verify cache '{SelectedCache.DisplayName}'.";
            RefreshConnectionState();
            return;
        }

        _client = client;
        ConnectionSummary = $"Connected to '{SelectedCache.DisplayName}' (db {SelectedCache.Database}).";
        RefreshConnectionState();
    }

    private static RedisCacheEntry CreateDemoCacheEntry() => new()
    {
        Id = DemoCacheId,
        DisplayName = DemoCacheDisplayName,
        ConnectionString = "demo://redis",
        Database = 0,
    };

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            return;
        }

        var previouslySelectedKey = SelectedKey;
        ResetKeyBrowser(clearSelection: true);
        ErrorMessage = null;
        RefreshAllState();

        var firstPage = new List<string>(ScanPageSize);
        var pattern = EffectivePattern;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _client.ScanKeysAsync(pattern, _scanCursor, ScanBatchSize, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            _scanCursor = result.Cursor;
            var appended = _scanAccumulator.AppendBatch(result.Keys, firstPage.Count);
            firstPage.AddRange(appended.VisibleKeys);
            _hasMoreKeys = _scanAccumulator.HasOverflow || (!result.IsComplete && _scanCursor != 0);

            if (_scanAccumulator.HasOverflow || firstPage.Count >= ScanPageSize || result.IsComplete || _scanCursor == 0)
            {
                break;
            }
        }
        while (true);

        foreach (var key in firstPage)
        {
            _keys.Add(key);
        }

        RebuildTree();
        UpdateScanSummary();
        await LoadVisibleKeyTypesAsync(firstPage, cancellationToken);

        if (previouslySelectedKey is not null && _keys.Contains(previouslySelectedKey, StringComparer.Ordinal))
        {
            SelectedKey = previouslySelectedKey;
            await RefreshSelectedKeyAsync();
            return;
        }

        await PublishSnapshotSafeAsync(recordRecent: false);
    }

    private async Task PublishSnapshotSafeAsync(bool recordRecent)
    {
        try
        {
            var snapshot = BuildSnapshot();
            if (snapshot is null)
            {
                _workspaceService.ClearCurrentSnapshot("redis");
                return;
            }

            await _workspaceService.PublishSnapshotAsync(snapshot, recordRecent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Publishing the Redis workspace snapshot failed.");
        }
    }

    private WorkspaceSnapshot? BuildSnapshot()
    {
        if (SelectedCache is null)
        {
            return null;
        }

        var resource = new OperatorResourceReference
        {
            Key = $"redis:{SelectedCache.Id}",
            Area = "redis",
            Kind = "cache",
            DisplayName = SelectedCache.DisplayName,
            DisplayPath = SelectedCache.DisplayName,
            Summary = $"DB {SelectedCache.Database}",
            Icon = "⚡",
        };

        var restoreState = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cacheId"] = SelectedCache.Id,
            ["pattern"] = EffectivePattern,
            ["separator"] = EffectiveSeparator,
        };

        if (!string.IsNullOrWhiteSpace(SelectedKey))
        {
            resource.Key = $"redis:{SelectedCache.Id}:{SelectedKey}";
            resource.Kind = "key";
            resource.DisplayName = SelectedKey;
            resource.DisplayPath = $"{SelectedCache.DisplayName}/{SelectedKey}";
            resource.Summary = SelectedKeyInfo is null
                ? $"DB {SelectedCache.Database}"
                : $"{SelectedKeyInfo.Type} · DB {SelectedCache.Database}";
            resource.Icon = "🔑";
            restoreState["selectedKey"] = SelectedKey;
        }

        return new WorkspaceSnapshot
        {
            Resource = resource,
            RestoreState = restoreState,
        };
    }

    private async Task RestoreWorkspaceAsync(WorkspaceSnapshot snapshot)
    {
        if (_isDisposed)
        {
            return;
        }

        await _appState.WhenInitializedAsync();
        SyncConfigState();

        if (CacheEntries.Count == 0)
        {
            return;
        }

        var cacheId = snapshot.RestoreState.TryGetValue("cacheId", out var restoredCacheId)
            ? restoredCacheId
            : SelectedCache?.Id;
        var restoredPattern = snapshot.RestoreState.TryGetValue("pattern", out var pattern)
            ? pattern
            : EffectivePattern;
        var restoredSeparator = snapshot.RestoreState.TryGetValue("separator", out var separator)
            ? separator
            : EffectiveSeparator;
        var restoredKey = snapshot.RestoreState.TryGetValue("selectedKey", out var selectedKey)
            ? selectedKey
            : null;

        var targetCache = CacheEntries.FirstOrDefault(candidate => string.Equals(candidate.Id, cacheId, StringComparison.Ordinal))
                          ?? CacheEntries.FirstOrDefault();
        if (targetCache is null)
        {
            return;
        }

        _suppressSelectionSideEffects = true;
        try
        {
            SelectedCache = targetCache;
            PatternInput = string.IsNullOrWhiteSpace(restoredPattern) ? "*" : restoredPattern;
            SeparatorInput = NormalizeSeparator(restoredSeparator);
            SelectedKey = restoredKey;
        }
        finally
        {
            _suppressSelectionSideEffects = false;
        }

        if (CurrentRedisConfig is RedisConfig redisConfig)
        {
            redisConfig.ActiveCacheId = targetCache.Id;
            redisConfig.NamespaceSeparator = EffectiveSeparator;
            await _appState.SaveConfigAsync();
        }

        await ReloadAsync();

        if (!string.IsNullOrWhiteSpace(restoredKey) && !_keys.Contains(restoredKey, StringComparer.Ordinal))
        {
            PatternInput = EscapeScanPattern(restoredKey);
            await ReloadAsync();
        }
    }

    private static string EscapeScanPattern(string key) =>
        key
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("?", "\\?", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);

    private async Task LoadVisibleKeyTypesAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        if (_client is null || keys.Count == 0)
        {
            return;
        }

        foreach (var batch in keys.Chunk(KeyTypeBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolvedTypes = await Task.WhenAll(batch.Select(key => ResolveKeyTypeAsync(key, cancellationToken)));

            foreach (var (key, type) in resolvedTypes)
            {
                if (_keys.Contains(key, StringComparer.Ordinal))
                {
                    _keyTypes[key] = type;
                }
            }
        }

        RebuildTree();
    }

    private async Task<(string Key, string Type)> ResolveKeyTypeAsync(string key, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            return (key, "?");
        }

        try
        {
            var resolvedType = await _client.GetKeyTypeAsync(key, cancellationToken);
            return (key, resolvedType);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (key, "?");
        }
    }

    private async Task ResetLoadTokenAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        if (!_loadCts.IsCancellationRequested)
        {
            await CancelTokenAsync(_loadCts);
        }

        if (_isDisposed)
        {
            return;
        }

        _loadCts.Dispose();
        _loadCts = new CancellationTokenSource();
    }

    private async Task ResetDetailTokenAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        if (!_detailCts.IsCancellationRequested)
        {
            await CancelTokenAsync(_detailCts);
        }

        if (_isDisposed)
        {
            return;
        }

        _detailCts.Dispose();
        _detailCts = new CancellationTokenSource();
    }

    private static async Task CancelTokenAsync(CancellationTokenSource source)
    {
        try
        {
            await source.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void DisposeClient()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            _client.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Disposing the Redis client failed.");
        }
        finally
        {
            _client = null;
            RefreshConnectionState();
        }
    }

    private void ResetKeyBrowser(bool clearSelection)
    {
        _scanCursor = 0;
        _hasMoreKeys = false;
        _keys.Clear();
        _keyTypes.Clear();
        _expandedPrefixes.Clear();
        _scanAccumulator.Reset();
        TreeRows.Clear();
        ResetSelectionState(clearSelectionMode: clearSelection);
        InvalidateAnalysisState();
        UpdateScanSummary();

        if (clearSelection)
        {
            SelectedKey = null;
        }
    }

    private void ClearDetailCollections()
    {
        SelectedKeyInfo = null;
        SelectedStringEditorValue = string.Empty;
        IsSelectedStringBinary = false;
        HashFields.Clear();
        CollectionItems.Clear();
        SortedSetEntries.Clear();
        HashFieldEditorName = string.Empty;
        HashFieldEditorValue = string.Empty;
        SortedSetMemberEditor = string.Empty;
        SortedSetScoreEditor = 0;
        _listOffset = 0;
        _setCursor = 0;
        _hasMoreItems = false;
        IsDeleteConfirmationArmed = false;
    }

    private void RebuildTree()
    {
        var nodes = RedisKeyGrouper.BuildNamespaceTree(_keys, EffectiveSeparator);
        ExpandSelectedKeyPath();
        TreeRows.Clear();

        foreach (var node in nodes)
        {
            AppendTreeRows(node, depth: 0);
        }

        RefreshBrowserState();
    }

    private void AppendTreeRows(NamespaceNode node, int depth)
    {
        var isExpanded = !node.IsKey && _expandedPrefixes.Contains(node.FullPrefix);
        var keyType = node.IsKey && node.FullKey is not null && _keyTypes.TryGetValue(node.FullKey, out var resolvedType)
            ? resolvedType
            : string.Empty;
        var selectionKeys = CollectSelectionKeys(node);
        var selectedKeyCount = selectionKeys.Count(key => _selectedKeys.Contains(key));

        TreeRows.Add(new RedisTreeRowViewModel(
            rowId: node.IsKey ? node.FullKey ?? node.FullPrefix : node.FullPrefix,
            displayName: node.Name,
            isKey: node.IsKey,
            fullKey: node.FullKey,
            prefix: node.FullPrefix,
            keyCount: node.KeyCount,
            depth: depth,
            canExpand: !node.IsKey && node.Children.Count > 0,
            isExpanded: isExpanded,
            isSelected: node.IsKey && string.Equals(node.FullKey, SelectedKey, StringComparison.Ordinal),
            keyType: keyType,
            selectionKeys: selectionKeys,
            isSelectionMode: IsSelectionMode,
            selectedKeyCount: selectedKeyCount));

        if (!node.IsKey && isExpanded)
        {
            foreach (var child in node.Children)
            {
                AppendTreeRows(child, depth + 1);
            }
        }
    }

    private void ExpandSelectedKeyPath()
    {
        if (SelectedKey is null)
        {
            return;
        }

        var separator = EffectiveSeparator;
        if (string.IsNullOrWhiteSpace(separator))
        {
            return;
        }

        var segments = SelectedKey.Split(separator, StringSplitOptions.None);
        if (segments.Length <= 1)
        {
            return;
        }

        var currentPrefix = segments[0];
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (index > 0)
            {
                currentPrefix = $"{currentPrefix}{separator}{segments[index]}";
            }

            _expandedPrefixes.Add(currentPrefix);
        }
    }

    private void UpdateScanSummary()
    {
        var pattern = EffectivePattern;
        ScanSummary = _keys.Count switch
        {
            0 => $"No keys loaded for pattern '{pattern}'.",
            1 when _hasMoreKeys => $"1 key loaded for pattern '{pattern}'. More matches are available.",
            1 => $"1 key loaded for pattern '{pattern}'.",
            _ when _hasMoreKeys => $"{_keys.Count} keys loaded for pattern '{pattern}'. More matches are available.",
            _ => $"{_keys.Count} keys loaded for pattern '{pattern}'.",
        };

        RefreshBrowserState();
    }

    private string BuildDetailStatus()
    {
        if (SelectedKey is null)
        {
            return "Select a key to inspect metadata, TTL, and value content.";
        }

        if (IsDetailLoading)
        {
            return "Loading key metadata and value content.";
        }

        if (SelectedKeyInfo is null)
        {
            return "Key detail is unavailable.";
        }

        return SelectedKeyInfo.Type switch
        {
            "string" when IsSelectedStringBinary => "Binary string value detected. Inline editing is disabled.",
            "string" => "String value loaded.",
            "hash" => HashFields.Count == 1 ? "1 hash field loaded." : $"{HashFields.Count} hash fields loaded.",
            "list" => CollectionSectionStatus,
            "set" => CollectionSectionStatus,
            "zset" => SortedSetStatus,
            _ => "Key metadata loaded.",
        };
    }

    private static string NormalizeSeparator(string? separator)
    {
        var trimmed = separator?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "-" : trimmed;
    }

    private void SyncSelectedTtlState(RedisKeyInfo? value)
    {
        if (string.IsNullOrWhiteSpace(SelectedKey) || value is null)
        {
            _ttlDisplayed = null;
            if (string.IsNullOrWhiteSpace(SelectedKey))
            {
                _ttlTrackedKey = null;
                _ttlOriginal = null;
            }

            ResetTtlCountdownToken();
            RefreshTtlVisualizationState();
            return;
        }

        if (!string.Equals(_ttlTrackedKey, SelectedKey, StringComparison.Ordinal))
        {
            _ttlTrackedKey = SelectedKey;
            _ttlOriginal = value.Ttl;
        }
        else if (_ttlOriginal is null && value.Ttl is not null)
        {
            _ttlOriginal = value.Ttl;
        }

        _ttlDisplayed = value.Ttl;
        RestartTtlCountdown();
        RefreshTtlVisualizationState();
    }

    private void RestartTtlCountdown()
    {
        ResetTtlCountdownToken();

        if (_isDisposed || string.IsNullOrWhiteSpace(_ttlTrackedKey) || _ttlDisplayed is not { } ttl || ttl <= TimeSpan.Zero)
        {
            return;
        }

        var trackedKey = _ttlTrackedKey;
        var cancellationToken = _ttlCountdownCts.Token;
        _ = RunTtlCountdownAsync(trackedKey, cancellationToken);
    }

    private async Task RunTtlCountdownAsync(string trackedKey, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TtlCountdownInterval);
        var ticksUntilRefresh = TtlServerRefreshTickBudget;

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_isDisposed)
                {
                    return;
                }

                ExecuteOnUiThread(() =>
                {
                    if (_isDisposed || cancellationToken.IsCancellationRequested || !string.Equals(trackedKey, SelectedKey, StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (_ttlDisplayed is { } ttl && ttl > TimeSpan.Zero)
                    {
                        var nextTtl = ttl - TtlCountdownInterval;
                        _ttlDisplayed = nextTtl > TimeSpan.Zero ? nextTtl : TimeSpan.Zero;
                        RefreshTtlVisualizationState();
                    }
                });

                ticksUntilRefresh--;
                if (ticksUntilRefresh <= 0)
                {
                    ticksUntilRefresh = TtlServerRefreshTickBudget;
                    ExecuteOnUiThread(() =>
                    {
                        if (_isDisposed || cancellationToken.IsCancellationRequested || !string.Equals(trackedKey, SelectedKey, StringComparison.Ordinal) || IsWorking)
                        {
                            return;
                        }

                        _ = RefreshSelectedKeyAsync();
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ResetTtlCountdownToken(bool recreateToken = true)
    {
        try
        {
            if (!_ttlCountdownCts.IsCancellationRequested)
            {
                _ttlCountdownCts.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
        }

        _ttlCountdownCts.Dispose();
        if (recreateToken)
        {
            _ttlCountdownCts = new CancellationTokenSource();
        }
    }

    private TtlVisualState GetSelectedTtlVisualState() => TtlFormatter.GetVisualState(_ttlDisplayed, _ttlOriginal);

    private void RefreshTtlVisualizationState()
    {
        OnPropertyChanged(nameof(SelectedTtlText));
        OnPropertyChanged(nameof(SelectedTtlProgressValue));
        OnPropertyChanged(nameof(SelectedTtlProgressVisibility));
        OnPropertyChanged(nameof(SelectedTtlHealthyProgressVisibility));
        OnPropertyChanged(nameof(SelectedTtlWarningProgressVisibility));
        OnPropertyChanged(nameof(SelectedTtlCriticalProgressVisibility));
    }

    private void ExecuteOnUiThread(Action action)
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() => action());
    }

    private static string FormatTtl(TimeSpan? ttl)
    {
        if (ttl is null)
        {
            return "No expiry";
        }

        if (ttl <= TimeSpan.Zero)
        {
            return "Expired";
        }

        return FormatDuration(ttl.Value);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "0s";
        }

        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        }

        return $"{Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds))}s";
    }

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null)
        {
            return "Unavailable";
        }

        var value = bytes.Value;
        if (value < 1024)
        {
            return $"{value} B";
        }

        if (value < 1024 * 1024)
        {
            return $"{value / 1024d:F1} KB";
        }

        if (value < 1024L * 1024 * 1024)
        {
            return $"{value / 1024d / 1024d:F1} MB";
        }

        return $"{value / 1024d / 1024d / 1024d:F1} GB";
    }

    private string BuildCollectionStatus(string singularLabel, int loadedCount)
    {
        var noun = loadedCount == 1 ? singularLabel : $"{singularLabel}s";
        var suffix = _hasMoreItems ? " More items are available." : string.Empty;
        return $"{loadedCount} {noun} loaded.{suffix}";
    }

    private void CaptureLoadError(string message, Exception ex)
    {
        ErrorMessage = ex.Message;
        _logger.LogError(ex, message);
        RefreshAllState();
    }

    private void CaptureActionError(string message, Exception ex)
    {
        ErrorMessage = ex.Message;
        _logger.LogError(ex, message);
        _notifications.ShowError(message, ex: ex);
        RefreshAllState();
    }

    private void RefreshConnectionState()
    {
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(HasConfiguredCaches));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(ShowNotConfiguredState));
        OnPropertyChanged(nameof(ShowTreeEmptyState));
        OnPropertyChanged(nameof(HeaderStatusRowVisibility));
        OnPropertyChanged(nameof(HeaderMessagesVisibility));
        OnPropertyChanged(nameof(CanReload));
        OnPropertyChanged(nameof(CanChangeCache));
        OnPropertyChanged(nameof(CanEditPatternInput));
        OnPropertyChanged(nameof(CanEditSeparatorInput));
        RefreshAnalyticsState();
        RefreshSelectionState();
        RefreshBrowserState();
    }

    private void RefreshBrowserState()
    {
        OnPropertyChanged(nameof(ShowTreeEmptyState));
        OnPropertyChanged(nameof(LoadMoreKeysLabel));
        OnPropertyChanged(nameof(LoadMoreKeysVisibility));
        OnPropertyChanged(nameof(CanLoadMoreKeys));
        OnPropertyChanged(nameof(ScanSummary));
        RefreshAnalyticsState();
        RefreshSelectionState();
    }

    private void RefreshDetailState()
    {
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(ShowSelectionEmptyState));
        OnPropertyChanged(nameof(DetailLoadingVisibility));
        OnPropertyChanged(nameof(DetailContentVisibility));
        OnPropertyChanged(nameof(StringSectionVisibility));
        OnPropertyChanged(nameof(StringEditorVisibility));
        OnPropertyChanged(nameof(HashSectionVisibility));
        OnPropertyChanged(nameof(CollectionSectionVisibility));
        OnPropertyChanged(nameof(SortedSetSectionVisibility));
        OnPropertyChanged(nameof(ShowHashEmptyState));
        OnPropertyChanged(nameof(DetailTitle));
        OnPropertyChanged(nameof(DetailStatusText));
        OnPropertyChanged(nameof(SelectedTypeText));
        RefreshTtlVisualizationState();
        OnPropertyChanged(nameof(SelectedMemoryText));
        OnPropertyChanged(nameof(SelectedEncodingText));
        OnPropertyChanged(nameof(SelectedFrequencyText));
        OnPropertyChanged(nameof(SelectedIdleText));
        OnPropertyChanged(nameof(CollectionSectionTitle));
        OnPropertyChanged(nameof(CollectionSectionStatus));
        OnPropertyChanged(nameof(SortedSetStatus));
        OnPropertyChanged(nameof(LoadMoreItemsLabel));
        OnPropertyChanged(nameof(CanRefreshSelectedKey));
        OnPropertyChanged(nameof(CanRenameSelectedKey));
        OnPropertyChanged(nameof(CanDeleteSelectedKey));
        OnPropertyChanged(nameof(DeleteSelectedLabel));
        OnPropertyChanged(nameof(DeleteSelectedCancelVisibility));
        OnPropertyChanged(nameof(CanApplyTtl));
        OnPropertyChanged(nameof(CanRemoveTtl));
        OnPropertyChanged(nameof(CanSaveStringValue));
        OnPropertyChanged(nameof(CanUpsertHashField));
        OnPropertyChanged(nameof(CanApplySortedSetScore));
        OnPropertyChanged(nameof(CanLoadMoreItems));
    }

    private void RefreshAllState()
    {
        RefreshConnectionState();
        RefreshDetailState();
        OnPropertyChanged(nameof(ErrorVisibility));
        RefreshAnalyticsState();
        RefreshSelectionState();
    }
}