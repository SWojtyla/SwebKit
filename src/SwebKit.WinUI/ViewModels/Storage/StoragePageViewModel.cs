using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;
using SwebKit.WinUI.ViewModels.Settings;
using Windows.ApplicationModel.DataTransfer;

namespace SwebKit.WinUI.ViewModels.Storage;

public sealed partial class StoragePageViewModel : ObservableObject, IAsyncDisposable
{
    private const int InitialPreviewByteLimit = 524_288;
    private const int ExpandedPreviewByteLimit = 2_097_152;

    private readonly AppStateService _appState;
    private readonly IStorageClientFactory _storageClientFactory;
    private readonly DemoStorageClient _demoStorageClient;
    private readonly INotificationService _notifications;
    private readonly OperatorWorkspaceService _workspaceService;
    private readonly IShellNavigationService _navigation;
    private readonly ILogger<StoragePageViewModel> _logger;

    private CancellationTokenSource _refreshCts = new();
    private CancellationTokenSource _downloadCts = new();
    private IStorageClient? _client;
    private StorageCapabilities? _storageCapabilities;
    private BlobProperties? _selectedBlobProperties;
    private readonly HashSet<string> _selectedBlobNames = new(StringComparer.Ordinal);
    private bool _loaded;
    private bool _isDisposed;
    private bool _suppressAccountSelectionSideEffects;

    public StoragePageViewModel(
        AppStateService appState,
        IStorageClientFactory storageClientFactory,
        DemoStorageClient demoStorageClient,
        INotificationService notifications,
        OperatorWorkspaceService workspaceService,
        IShellNavigationService navigation,
        ILogger<StoragePageViewModel> logger)
    {
        _appState = appState;
        _storageClientFactory = storageClientFactory;
        _demoStorageClient = demoStorageClient;
        _notifications = notifications;
        _workspaceService = workspaceService;
        _navigation = navigation;
        _logger = logger;

        Accounts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAccounts));
            OnPropertyChanged(nameof(HasMultipleAccounts));
            OnPropertyChanged(nameof(AccountCountLabel));
            OnPropertyChanged(nameof(ShowNotConfiguredState));
        };

        Containers.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ContainerCountLabel));
            OnPropertyChanged(nameof(ShowContainerEmptyState));
            OnPropertyChanged(nameof(FilteredContainers));
        };

        Blobs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(BlobCountLabel));
            OnPropertyChanged(nameof(ShowBlobEmptyState));
            OnPropertyChanged(nameof(FilteredBlobs));
            RefreshBulkSelectionState();
        };

        BlobVersions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasBlobVersions));
            OnPropertyChanged(nameof(CanRestoreVersions));
            OnPropertyChanged(nameof(VersionListVisibility));
            OnPropertyChanged(nameof(VersionEmptyVisibility));
            OnPropertyChanged(nameof(RestoreVersionActionVisibility));
        };

        MetadataRows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMetadataRows));
            OnPropertyChanged(nameof(MetadataEmptyVisibility));
        };

        TagRows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTagRows));
            OnPropertyChanged(nameof(TagsEmptyVisibility));
        };

        _workspaceService.RegisterRestoreHandler("storage", RestoreWorkspaceAsync);
    }

    public ObservableCollection<StorageAccountItemViewModel> Accounts { get; } = [];

    public ObservableCollection<StorageContainerItemViewModel> Containers { get; } = [];

    public ObservableCollection<StorageBlobEntryViewModel> Blobs { get; } = [];

    public ObservableCollection<StorageBlobVersionViewModel> BlobVersions { get; } = [];

    public ObservableCollection<StorageBreadcrumbItemViewModel> Breadcrumbs { get; } = [];

    public ObservableCollection<StoragePropertyRowViewModel> BlobPropertyRows { get; } = [];

    public ObservableCollection<StorageKeyValueRowViewModel> MetadataRows { get; } = [];

    public ObservableCollection<StorageKeyValueRowViewModel> TagRows { get; } = [];

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingContainers { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingBlobs { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingBlobDetail { get; set; }

    [ObservableProperty]
    public partial bool IsDownloadingBlob { get; set; }

    [ObservableProperty]
    public partial bool IsDownloadIndeterminate { get; set; }

    [ObservableProperty]
    public partial bool IsSelectionMode { get; set; }

    [ObservableProperty]
    public partial bool IsBulkDownloading { get; set; }

    [ObservableProperty]
    public partial string ContainerFilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BlobFilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BulkSelectionSummaryText { get; set; } = "No loaded blobs selected for ZIP download.";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? BlobListErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? BlobDetailErrorMessage { get; set; }

    [ObservableProperty]
    public partial string ConnectionSummary { get; set; } = "Storage is not configured.";

    [ObservableProperty]
    public partial StorageAccountItemViewModel? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial StorageContainerItemViewModel? SelectedContainer { get; set; }

    [ObservableProperty]
    public partial StorageBlobEntryViewModel? SelectedBlob { get; set; }

    [ObservableProperty]
    public partial string CurrentPrefix { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CurrentFolderLabel { get; set; } = "Root";

    [ObservableProperty]
    public partial string? ContinuationToken { get; set; }

    [ObservableProperty]
    public partial string SelectedBlobTitle { get; set; } = "Select a blob";

    [ObservableProperty]
    public partial string SelectedBlobSubtitle { get; set; } = "Preview text-friendly content and inspect blob properties from here.";

    [ObservableProperty]
    public partial string BlobDetailStatusText { get; set; } = "Select a blob to inspect properties, metadata, tags, and content preview.";

    [ObservableProperty]
    public partial string? PreviewContent { get; set; }

    [ObservableProperty]
    public partial string? PreviewInfoMessage { get; set; }

    [ObservableProperty]
    public partial bool PreviewIsBinary { get; set; }

    [ObservableProperty]
    public partial bool CanLoadExpandedPreview { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingVersions { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingVersionComparison { get; set; }

    [ObservableProperty]
    public partial string VersionStatusText { get; set; } = "Select a blob to inspect version history when the account supports it.";

    [ObservableProperty]
    public partial string? VersionErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? VersionComparisonSummary { get; set; }

    [ObservableProperty]
    public partial string? VersionComparisonText { get; set; }

    [ObservableProperty]
    public partial string? SelectedBlobUrl { get; set; }

    [ObservableProperty]
    public partial string ActiveDownloadLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DownloadProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double DownloadProgressPercent { get; set; }

    public bool HasAccounts => Accounts.Count > 0;

    public bool HasMultipleAccounts => Accounts.Count > 1;

    public bool HasSelectedContainer => SelectedContainer is not null;

    public bool HasSelectedBlob => SelectedBlob is not null;

    public bool HasBlobVersions => BlobVersions.Count > 0;

    public int SelectedBlobCount => _selectedBlobNames.Count;

    public bool HasMetadataRows => MetadataRows.Count > 0;

    public bool HasTagRows => TagRows.Count > 0;

    public bool AllowMutations => SelectedAccount?.Config.AllowMutations == true;

    public bool CanUploadToSelectedContainer => AllowMutations && SelectedContainer is not null;

    public bool CanCopySelectedContainerSas => SelectedContainer is not null;

    public bool CanCopySelectedBlob => AllowMutations && SelectedBlob is not null;

    public bool CanEditSelectedBlobMetadata => AllowMutations && SelectedBlob is not null && _selectedBlobProperties is not null && !IsLoadingBlobDetail;

    public bool CanCopySelectedBlobPath => SelectedBlob is not null;

    public bool CanCopyPreviewContent => HasSelectedBlob && !PreviewIsBinary && !string.IsNullOrWhiteSpace(PreviewContent);

    public IReadOnlyList<StorageContainerItemViewModel> FilteredContainers => string.IsNullOrWhiteSpace(ContainerFilterText)
        ? [.. Containers]
        : [.. Containers.Where(container => container.Name.Contains(ContainerFilterText, StringComparison.OrdinalIgnoreCase))];

    public IReadOnlyList<StorageBlobEntryViewModel> FilteredBlobs => string.IsNullOrWhiteSpace(BlobFilterText)
        ? [.. Blobs]
        : [.. Blobs.Where(blob => blob.FullName.Contains(BlobFilterText, StringComparison.OrdinalIgnoreCase))];

    public bool CanRestoreVersions => AllowMutations && (_storageCapabilities?.CanRestore == true || HasBlobVersions);

    public bool ShowNotConfiguredState => !IsRefreshing && !HasAccounts && !_appState.UseDemoData;

    public bool ShowContainerEmptyState => _client is not null && !IsLoadingContainers && Containers.Count == 0 && string.IsNullOrWhiteSpace(ErrorMessage);

    public bool ShowBlobEmptyState => HasSelectedContainer && !IsLoadingBlobs && Blobs.Count == 0 && string.IsNullOrWhiteSpace(BlobListErrorMessage);

    public string AccountCountLabel => Accounts.Count == 1 ? "1 account" : $"{Accounts.Count} accounts";

    public string ContainerCountLabel => Containers.Count == 1 ? "1 container" : $"{Containers.Count} containers";

    public string BlobCountLabel => Blobs.Count == 1 ? "1 item" : $"{Blobs.Count} items";

    public string SelectedContainerTitle => SelectedContainer is null ? "Select a container" : $"Blobs in {SelectedContainer.Name}";

    public string SelectedContainerSubtitle => SelectedContainer is null
        ? "Choose a container to browse virtual folders, preview blobs, and load more items."
        : $"{CurrentFolderLabel} · {BlobCountLabel}";

    public Visibility GlobalErrorVisibility => string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility BlobWorkspaceVisibility => HasSelectedContainer ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SelectContainerHintVisibility => HasSelectedContainer ? Visibility.Collapsed : Visibility.Visible;

    public Visibility BlobListErrorVisibility => string.IsNullOrWhiteSpace(BlobListErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility BlobDetailVisibility => HasSelectedBlob ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BlobDetailPlaceholderVisibility => HasSelectedBlob ? Visibility.Collapsed : Visibility.Visible;

    public Visibility BlobDetailErrorVisibility => string.IsNullOrWhiteSpace(BlobDetailErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PreviewVisibility => HasSelectedBlob && !IsLoadingBlobDetail && !PreviewIsBinary && PreviewContent is not null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility UploadActionVisibility => CanUploadToSelectedContainer
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ContainerSasActionVisibility => CanCopySelectedContainerSas
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility CopyBlobActionVisibility => CanCopySelectedBlob
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility EditMetadataActionVisibility => CanEditSelectedBlobMetadata
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility CopyBlobPathActionVisibility => CanCopySelectedBlobPath
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility CopyPreviewActionVisibility => CanCopyPreviewContent
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility BinaryPreviewVisibility => HasSelectedBlob && !IsLoadingBlobDetail && PreviewIsBinary
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility VersionListVisibility => HasSelectedBlob && HasBlobVersions
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility VersionEmptyVisibility => HasSelectedBlob && !IsLoadingVersions && !HasBlobVersions && string.IsNullOrWhiteSpace(VersionErrorMessage)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility VersionErrorVisibility => string.IsNullOrWhiteSpace(VersionErrorMessage)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility VersionComparisonVisibility => string.IsNullOrWhiteSpace(VersionComparisonSummary) && string.IsNullOrWhiteSpace(VersionComparisonText)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility RestoreVersionActionVisibility => CanRestoreVersions ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BulkToolbarVisibility => IsSelectionMode ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BulkSelectionSummaryVisibility => IsSelectionMode ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BulkSelectionToggleVisibility => IsSelectionMode ? Visibility.Visible : Visibility.Collapsed;

    public string SelectionModeButtonLabel => IsSelectionMode ? "Done selecting" : "Select blobs";

    public string BulkDownloadButtonLabel => IsBulkDownloading ? "Downloading ZIP..." : "Download as ZIP";

    public bool CanRefreshCurrentFolder => HasSelectedContainer && !IsBulkDownloading;

    public bool CanInteractWithBlobEntries => !IsBulkDownloading;

    public bool CanToggleSelectionMode => HasSelectedContainer && Blobs.Any(static blob => !blob.IsPrefix) && !IsBulkDownloading && !IsDownloadingBlob;

    public bool CanSelectAllLoadedBlobs => IsSelectionMode && Blobs.Any(static blob => !blob.IsPrefix) && !IsBulkDownloading && !IsDownloadingBlob;

    public bool CanClearSelectedBlobs => IsSelectionMode && SelectedBlobCount > 0 && !IsBulkDownloading;

    public bool CanDownloadSelectedBlobs => IsSelectionMode && SelectedBlobCount > 0 && !IsBulkDownloading && !IsDownloadingBlob;

    public Visibility PreviewInfoVisibility => string.IsNullOrWhiteSpace(PreviewInfoMessage) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility LoadMoreVisibility => !IsLoadingBlobs && !string.IsNullOrWhiteSpace(ContinuationToken)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility DownloadVisibility => IsDownloadingBlob ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LoadExpandedPreviewVisibility => CanLoadExpandedPreview ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MetadataEmptyVisibility => HasMetadataRows ? Visibility.Collapsed : Visibility.Visible;

    public Visibility TagsEmptyVisibility => HasTagRows ? Visibility.Collapsed : Visibility.Visible;

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
        await _workspaceService.ApplyPendingRestoreAsync("storage");
    }

    public async Task<BlobMutationResult> UploadBlobAsync(
        string blobName,
        Stream source,
        bool overwrite,
        string? contentType = null,
        CancellationToken ct = default)
    {
        if (_client is null || SelectedContainer is null)
        {
            return new BlobMutationResult(false, ErrorMessage: "Select a container before uploading a blob.");
        }

        if (!AllowMutations)
        {
            return new BlobMutationResult(false, ErrorMessage: "Mutations are disabled for the selected storage account.");
        }

        var trimmedBlobName = blobName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedBlobName))
        {
            return new BlobMutationResult(false, ErrorMessage: "Enter a blob name before uploading.");
        }

        try
        {
            var result = await _client.UploadBlobAsync(
                new BlobUploadOptions(SelectedContainer.Name, trimmedBlobName, overwrite, contentType),
                source,
                ct: ct);

            HandleMutationFeedback(
                result,
                successTitle: "Blob uploaded",
                successStatus: $"Uploaded to {result.ResultBlobPath ?? $"{SelectedContainer.Name}/{trimmedBlobName}"}.",
                failureStatusPrefix: "Upload failed");

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage upload failed for container {ContainerName} and blob {BlobName}.", SelectedContainer.Name, trimmedBlobName);
            return HandleMutationException("Blob upload failed", "Upload failed", ex);
        }
    }

    public async Task<BlobMutationResult> CopySelectedBlobAsync(
        string destinationContainer,
        string destinationBlobName,
        bool overwrite,
        string? sourceVersionId = null,
        CancellationToken ct = default)
    {
        if (_client is null || SelectedContainer is null || SelectedBlob is null)
        {
            return new BlobMutationResult(false, ErrorMessage: "Select a blob before copying it.");
        }

        if (!AllowMutations)
        {
            return new BlobMutationResult(false, ErrorMessage: "Mutations are disabled for the selected storage account.");
        }

        var trimmedDestinationContainer = destinationContainer?.Trim();
        var trimmedDestinationBlobName = destinationBlobName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedDestinationContainer))
        {
            return new BlobMutationResult(false, ErrorMessage: "Choose a destination container before copying the blob.");
        }

        if (string.IsNullOrWhiteSpace(trimmedDestinationBlobName))
        {
            return new BlobMutationResult(false, ErrorMessage: "Enter a destination blob name before copying the blob.");
        }

        try
        {
            var result = await _client.CopyBlobAsync(
                new BlobCopyOptions(
                    SelectedContainer.Name,
                    SelectedBlob.FullName,
                    trimmedDestinationContainer,
                    trimmedDestinationBlobName,
                    sourceVersionId,
                    overwrite),
                ct);

            HandleMutationFeedback(
                result,
                successTitle: "Blob copied",
                successStatus: $"Copied to {result.ResultBlobPath ?? $"{trimmedDestinationContainer}/{trimmedDestinationBlobName}"}.",
                failureStatusPrefix: "Copy failed");

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage copy failed for blob {BlobName}.", SelectedBlob.FullName);
            return HandleMutationException("Blob copy failed", "Copy failed", ex);
        }
    }

    public async Task<BlobMutationResult> SaveSelectedBlobMetadataAsync(
        IDictionary<string, string> metadata,
        CancellationToken ct = default)
    {
        if (_client is null || SelectedContainer is null || SelectedBlob is null)
        {
            return new BlobMutationResult(false, ErrorMessage: "Select a blob before updating metadata.");
        }

        if (!AllowMutations)
        {
            return new BlobMutationResult(false, ErrorMessage: "Mutations are disabled for the selected storage account.");
        }

        try
        {
            var etag = _selectedBlobProperties?.ETag;
            var result = await _client.SetBlobMetadataAsync(
                SelectedContainer.Name,
                SelectedBlob.FullName,
                metadata,
                etag,
                ct);

            if (result.Success)
            {
                var refreshedProperties = await _client.GetBlobPropertiesAsync(SelectedContainer.Name, SelectedBlob.FullName, ct);
                ApplyProperties(refreshedProperties);
            }

            HandleMutationFeedback(
                result,
                successTitle: "Metadata saved",
                successStatus: "Blob metadata updated.",
                failureStatusPrefix: "Metadata save failed");

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage metadata update failed for blob {BlobName}.", SelectedBlob.FullName);
            return HandleMutationException("Metadata save failed", "Metadata save failed", ex);
        }
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await _appState.WhenInitializedAsync();

        var preferredAccountId = SelectedAccount?.Id;
        var preferredContainer = SelectedContainer?.Name;
        var preferredPrefix = CurrentPrefix;
        var preferredBlobName = SelectedBlob?.FullName;

        await ResetRefreshTokenAsync();
        IsRefreshing = true;
        ErrorMessage = null;
        BlobListErrorMessage = null;
        BlobDetailErrorMessage = null;
        await Task.Yield();

        try
        {
            RebuildAccounts();

            if (!HasAccounts)
            {
                ClearAllState();
                ConnectionSummary = "No storage accounts are configured. Add one from Settings before opening this workspace.";
                _workspaceService.ClearCurrentSnapshot("storage");
                return;
            }

            var account = ResolvePreferredAccount(preferredAccountId);
            SetSelectedAccount(account);

            await LoadSelectedAccountAsync(
                account,
                preferredContainer,
                preferredPrefix,
                preferredBlobName,
                recordRecent: false,
                _refreshCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage page reload failed.");
            ErrorMessage = ex.Message;
            ConnectionSummary = "Storage reload failed.";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private Task OpenSettingsAsync()
    {
        _navigation.NavigateTo("settings", new SettingsNavigationRequest(SettingsSections.Storage));
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RefreshBlobsAsync()
    {
        if (SelectedContainer is null)
        {
            return;
        }

        try
        {
            await ResetRefreshTokenAsync();
            await SelectContainerCoreAsync(
                SelectedContainer.Name,
                CurrentPrefix,
                SelectedBlob?.FullName,
                recordRecent: false,
                _refreshCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private async Task SelectContainerAsync(StorageContainerItemViewModel? container)
    {
        if (container is null)
        {
            return;
        }

        try
        {
            await ResetRefreshTokenAsync();
            await SelectContainerCoreAsync(container.Name, string.Empty, requestedBlobName: null, recordRecent: true, _refreshCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private async Task OpenBlobEntryAsync(StorageBlobEntryViewModel? blob)
    {
        if (blob is null)
        {
            return;
        }

        try
        {
            await ResetRefreshTokenAsync();

            if (blob.IsPrefix)
            {
                await SelectContainerCoreAsync(SelectedContainer?.Name, blob.FullName, requestedBlobName: null, recordRecent: false, _refreshCts.Token);
                return;
            }

            await SelectBlobCoreAsync(blob.FullName, recordRecent: true, _refreshCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private async Task NavigateToBreadcrumbAsync(StorageBreadcrumbItemViewModel? breadcrumb)
    {
        if (breadcrumb is null || SelectedContainer is null)
        {
            return;
        }

        try
        {
            await ResetRefreshTokenAsync();
            await SelectContainerCoreAsync(SelectedContainer.Name, breadcrumb.Prefix, requestedBlobName: null, recordRecent: false, _refreshCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_client is null || SelectedContainer is null || string.IsNullOrWhiteSpace(ContinuationToken))
        {
            return;
        }

        try
        {
            await LoadBlobPageAsync(ContinuationToken, append: true, _refreshCts.Token);
            await PublishSnapshotAsync(recordRecent: false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private async Task DownloadSelectedBlobAsync()
    {
        if (SelectedBlob is null)
        {
            return;
        }

        await DownloadBlobCoreAsync(SelectedBlob.FullName, SelectedBlob.DisplayName);
    }

    [RelayCommand]
    private async Task DownloadBlobAsync(StorageBlobEntryViewModel? blob)
    {
        if (blob is null || blob.IsPrefix)
        {
            return;
        }

        await DownloadBlobCoreAsync(blob.FullName, blob.DisplayName);
    }

    [RelayCommand]
    private void ToggleSelectionMode()
    {
        IsSelectionMode = !IsSelectionMode;
        if (!IsSelectionMode)
        {
            _selectedBlobNames.Clear();
        }

        RefreshBulkSelectionState();
    }

    [RelayCommand]
    private void SelectAllLoadedBlobs()
    {
        _selectedBlobNames.Clear();

        foreach (var blob in Blobs.Where(static blob => !blob.IsPrefix))
        {
            _selectedBlobNames.Add(blob.FullName);
        }

        IsSelectionMode = _selectedBlobNames.Count > 0;
        RefreshBulkSelectionState();
    }

    [RelayCommand]
    private void ClearSelectedBlobs()
    {
        _selectedBlobNames.Clear();
        RefreshBulkSelectionState();
    }

    [RelayCommand]
    private void ToggleBlobSelection(StorageBlobEntryViewModel? blob)
    {
        if (blob is null || blob.IsPrefix)
        {
            return;
        }

        if (!_selectedBlobNames.Remove(blob.FullName))
        {
            _selectedBlobNames.Add(blob.FullName);
        }

        RefreshBulkSelectionState();
    }

    [RelayCommand]
    private async Task DownloadSelectedBlobsZipAsync()
    {
        if (_client is null || SelectedContainer is null || _selectedBlobNames.Count == 0)
        {
            return;
        }

        string? zipPath = null;

        try
        {
            await ResetDownloadTokenAsync();
            var downloadToken = _downloadCts.Token;

            IsBulkDownloading = true;
            BulkSelectionSummaryText = $"Creating a ZIP for {SelectedBlobCount} selected blob(s)...";
            await Task.Yield();

            zipPath = BuildZipDownloadPath(SelectedContainer.Name);

            await using var fileStream = File.Create(zipPath);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);
            var selectedBlobNames = _selectedBlobNames.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var blobName in selectedBlobNames)
            {
                var entryName = BuildUniqueZipEntryName(blobName, usedEntryNames);
                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await _client.DownloadBlobAsync(SelectedContainer.Name, blobName, entryStream, ct: downloadToken);
            }

            var downloadedBlobCount = SelectedBlobCount;
            var successMessage = $"Downloaded {downloadedBlobCount} blob(s) to {zipPath}.";
            _notifications.ShowSuccess("ZIP downloaded", Path.GetFileName(zipPath));
            _selectedBlobNames.Clear();
            RefreshBulkSelectionState();
            BulkSelectionSummaryText = successMessage;
        }
        catch (OperationCanceledException)
        {
            BulkSelectionSummaryText = "ZIP download cancelled.";
            DeletePartialDownload(zipPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage ZIP download failed for container {ContainerName}.", SelectedContainer.Name);
            BulkSelectionSummaryText = $"ZIP download failed: {ex.Message}";
            DeletePartialDownload(zipPath);
            _notifications.ShowError("ZIP download failed", ex: ex);
        }
        finally
        {
            IsBulkDownloading = false;
        }
    }

    [RelayCommand]
    private Task CopySelectedBlobUrlAsync() => CopyToClipboardAsync(() => SelectedBlobUrl, "Blob URL copied");

    [RelayCommand]
    private async Task CopySelectedBlobSasAsync()
    {
        if (_client is null || SelectedContainer is null || SelectedBlob is null)
        {
            return;
        }

        try
        {
            var sasUrl = await _client.GetBlobSasUrlAsync(SelectedContainer.Name, SelectedBlob.FullName, TimeSpan.FromHours(24), _refreshCts.Token);
            StorageClipboardHelper.CopyText(sasUrl);
            _notifications.ShowSuccess("SAS URL copied", SelectedBlob.DisplayName);
            BlobDetailStatusText = "Copied a 24-hour SAS URL to the clipboard.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage blob SAS copy failed for {BlobName}.", SelectedBlob.FullName);
            _notifications.ShowError("SAS URL copy failed", ex: ex);
            BlobDetailStatusText = ex.Message;
        }
    }

    [RelayCommand]
    private async Task CopySelectedContainerSasUrlAsync()
    {
        if (_client is null || SelectedContainer is null)
        {
            return;
        }

        try
        {
            var sasUrl = await _client.GetContainerSasUrlAsync(SelectedContainer.Name, TimeSpan.FromHours(24), _refreshCts.Token);
            StorageClipboardHelper.CopyText(sasUrl);
            _notifications.ShowSuccess("Container SAS copied", SelectedContainer.Name);
            ConnectionSummary = "Copied a 24-hour container SAS URL to the clipboard.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage container SAS copy failed for {ContainerName}.", SelectedContainer.Name);
            _notifications.ShowError("Container SAS copy failed", ex: ex);
            ConnectionSummary = ex.Message;
        }
    }

    [RelayCommand]
    private Task CopySelectedBlobPathAsync() => CopyToClipboardAsync(() => SelectedBlob?.FullName, "Blob path copied");

    [RelayCommand]
    private Task CopyPreviewContentAsync() => CopyToClipboardAsync(() => PreviewContent, "Preview content copied");

    [RelayCommand]
    private async Task LoadFullPreviewAsync()
    {
        if (_client is null || SelectedContainer is null || SelectedBlob is null || !CanLoadExpandedPreview)
        {
            return;
        }

        try
        {
            IsLoadingBlobDetail = true;
            BlobDetailErrorMessage = null;
            PreviewInfoMessage = null;
            await Task.Yield();

            var content = await _client.GetBlobContentAsync(
                SelectedContainer.Name,
                SelectedBlob.FullName,
                ExpandedPreviewByteLimit,
                _refreshCts.Token);

            ApplyPreview(content, SelectedBlob.FullName, fullPreviewRequested: true);
            BlobDetailStatusText = content.WasTruncated
                ? "Loaded a larger inline preview up to the 2 MB cap. Download for the full file."
                : "Loaded the full inline preview.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage full preview load failed for {BlobName}.", SelectedBlob.FullName);
            BlobDetailErrorMessage = ex.Message;
            BlobDetailStatusText = "Full preview load failed.";
        }
        finally
        {
            IsLoadingBlobDetail = false;
        }
    }

    [RelayCommand]
    private async Task DownloadVersionAsync(StorageBlobVersionViewModel? version)
    {
        if (SelectedBlob is null || version is null)
        {
            return;
        }

        await DownloadBlobCoreAsync(
            SelectedBlob.FullName,
            SelectedBlob.DisplayName,
            versionId: version.VersionId,
            totalBytes: version.ContentLength,
            progressLabel: $"Downloading version {version.ShortVersionId}");
    }

    [RelayCommand]
    private async Task CompareVersionAsync(StorageBlobVersionViewModel? version)
    {
        if (_client is null || SelectedContainer is null || SelectedBlob is null || version is null)
        {
            return;
        }

        var blobName = SelectedBlob.FullName;

        try
        {
            IsLoadingVersionComparison = true;
            VersionComparisonSummary = null;
            VersionComparisonText = null;
            VersionStatusText = $"Comparing version {version.ShortVersionId}...";
            await Task.Yield();

            var comparison = await _client.GetVersionComparisonAsync(
                SelectedContainer.Name,
                blobName,
                version.VersionId,
                ct: _refreshCts.Token);

            if (!IsCurrentSelectedBlob(blobName))
            {
                return;
            }

            VersionComparisonSummary = BuildVersionComparisonSummary(comparison);
            VersionComparisonText = string.IsNullOrWhiteSpace(comparison.TextDiff)
                ? "No inline text diff is available for this comparison."
                : comparison.TextDiff.ReplaceLineEndings("\n");
            VersionStatusText = $"Compared version {version.ShortVersionId}.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage version comparison failed for {BlobName} and version {VersionId}.", blobName, version.VersionId);

            if (!IsCurrentSelectedBlob(blobName))
            {
                return;
            }

            VersionComparisonSummary = "Version comparison failed.";
            VersionComparisonText = ex.Message;
            VersionStatusText = "Version comparison failed.";
        }
        finally
        {
            if (IsCurrentSelectedBlob(blobName))
            {
                IsLoadingVersionComparison = false;
            }
        }
    }

    [RelayCommand]
    private async Task RestoreVersionAsync(StorageBlobVersionViewModel? version)
    {
        if (_client is null || SelectedContainer is null || SelectedBlob is null || version is null || !CanRestoreVersions)
        {
            return;
        }

        var blobName = SelectedBlob.FullName;

        try
        {
            var result = await _client.RestoreBlobVersionAsync(SelectedContainer.Name, blobName, version.VersionId, _refreshCts.Token);

            if (!IsCurrentSelectedBlob(blobName))
            {
                return;
            }

            if (result.State != BlobRecoveryState.Restored)
            {
                VersionStatusText = $"Version restore failed: {result.ErrorMessage ?? result.State.ToString()}";
                _notifications.ShowError("Version restore failed", result.ErrorMessage ?? result.State.ToString());
                return;
            }

            await RefreshSelectedBlobDetailAsync(blobName, _refreshCts.Token);

            var statusMessage = result.ResultBlobPath is null
                ? $"Restored version {version.ShortVersionId}."
                : $"Restored version {version.ShortVersionId} to {result.ResultBlobPath}.";

            VersionStatusText = statusMessage;
            _notifications.ShowSuccess("Blob version restored", version.ShortVersionId);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage version restore failed for {BlobName} and version {VersionId}.", blobName, version.VersionId);

            if (!IsCurrentSelectedBlob(blobName))
            {
                return;
            }

            VersionStatusText = $"Version restore failed: {ex.Message}";
            _notifications.ShowError("Version restore failed", ex: ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _workspaceService.UnregisterRestoreHandler("storage");
        await CancelTokenAsync(_refreshCts);
        await CancelTokenAsync(_downloadCts);
        _refreshCts.Dispose();
        _downloadCts.Dispose();
    }

    partial void OnSelectedAccountChanged(StorageAccountItemViewModel? value)
    {
        OnPropertyChanged(nameof(AllowMutations));
        NotifyMutationActionStateChanged();
        OnPropertyChanged(nameof(CanRestoreVersions));

        if (_isDisposed || _suppressAccountSelectionSideEffects || !_loaded || value is null)
        {
            return;
        }

        _ = SwitchAccountAsync(value);
    }

    partial void OnSelectedContainerChanged(StorageContainerItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedContainer));
        NotifyMutationActionStateChanged();
        OnPropertyChanged(nameof(CanCopySelectedContainerSas));
        OnPropertyChanged(nameof(ContainerSasActionVisibility));
        OnPropertyChanged(nameof(CanRefreshCurrentFolder));
        OnPropertyChanged(nameof(BlobWorkspaceVisibility));
        OnPropertyChanged(nameof(SelectContainerHintVisibility));
        OnPropertyChanged(nameof(SelectedContainerTitle));
        OnPropertyChanged(nameof(SelectedContainerSubtitle));
        OnPropertyChanged(nameof(ShowBlobEmptyState));
    }

    partial void OnSelectedBlobChanged(StorageBlobEntryViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedBlob));
        NotifyMutationActionStateChanged();
        NotifyPreviewActionStateChanged();
        OnPropertyChanged(nameof(CanCopySelectedBlobPath));
        OnPropertyChanged(nameof(CopyBlobPathActionVisibility));
        OnPropertyChanged(nameof(BlobDetailVisibility));
        OnPropertyChanged(nameof(BlobDetailPlaceholderVisibility));
        OnPropertyChanged(nameof(PreviewVisibility));
        OnPropertyChanged(nameof(BinaryPreviewVisibility));
        OnPropertyChanged(nameof(VersionListVisibility));
        OnPropertyChanged(nameof(VersionEmptyVisibility));
    }

    partial void OnCurrentFolderLabelChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedContainerSubtitle));
    }

    partial void OnContinuationTokenChanged(string? value)
    {
        OnPropertyChanged(nameof(LoadMoreVisibility));
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(GlobalErrorVisibility));
        OnPropertyChanged(nameof(ShowContainerEmptyState));
    }

    partial void OnBlobListErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(BlobListErrorVisibility));
        OnPropertyChanged(nameof(ShowBlobEmptyState));
    }

    partial void OnBlobDetailErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(BlobDetailErrorVisibility));
    }

    partial void OnIsLoadingBlobDetailChanged(bool value)
    {
        NotifyMutationActionStateChanged();
        OnPropertyChanged(nameof(PreviewVisibility));
        OnPropertyChanged(nameof(BinaryPreviewVisibility));
    }

    partial void OnPreviewContentChanged(string? value)
    {
        OnPropertyChanged(nameof(PreviewVisibility));
        NotifyPreviewActionStateChanged();
    }

    partial void OnPreviewInfoMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(PreviewInfoVisibility));
    }

    partial void OnPreviewIsBinaryChanged(bool value)
    {
        OnPropertyChanged(nameof(PreviewVisibility));
        OnPropertyChanged(nameof(BinaryPreviewVisibility));
        NotifyPreviewActionStateChanged();
    }

    partial void OnCanLoadExpandedPreviewChanged(bool value)
    {
        OnPropertyChanged(nameof(LoadExpandedPreviewVisibility));
    }

    partial void OnIsLoadingVersionsChanged(bool value)
    {
        OnPropertyChanged(nameof(VersionEmptyVisibility));
    }

    partial void OnIsLoadingVersionComparisonChanged(bool value)
    {
        OnPropertyChanged(nameof(VersionComparisonVisibility));
    }

    partial void OnVersionErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(VersionErrorVisibility));
        OnPropertyChanged(nameof(VersionEmptyVisibility));
    }

    partial void OnVersionComparisonSummaryChanged(string? value)
    {
        OnPropertyChanged(nameof(VersionComparisonVisibility));
    }

    partial void OnVersionComparisonTextChanged(string? value)
    {
        OnPropertyChanged(nameof(VersionComparisonVisibility));
    }

    partial void OnIsLoadingContainersChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowContainerEmptyState));
    }

    partial void OnContainerFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredContainers));
    }

    partial void OnBlobFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredBlobs));
    }

    partial void OnIsLoadingBlobsChanged(bool value)
    {
        OnPropertyChanged(nameof(LoadMoreVisibility));
        OnPropertyChanged(nameof(ShowBlobEmptyState));
        RefreshBulkSelectionState();
    }

    partial void OnIsDownloadingBlobChanged(bool value)
    {
        OnPropertyChanged(nameof(DownloadVisibility));
        OnPropertyChanged(nameof(CanToggleSelectionMode));
        OnPropertyChanged(nameof(CanSelectAllLoadedBlobs));
        OnPropertyChanged(nameof(CanDownloadSelectedBlobs));
    }

    partial void OnIsRefreshingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNotConfiguredState));
    }

    partial void OnIsSelectionModeChanged(bool value)
    {
        RefreshBulkSelectionState();
    }

    partial void OnIsBulkDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(BulkDownloadButtonLabel));
        OnPropertyChanged(nameof(CanRefreshCurrentFolder));
        OnPropertyChanged(nameof(CanInteractWithBlobEntries));
        OnPropertyChanged(nameof(CanToggleSelectionMode));
        OnPropertyChanged(nameof(CanSelectAllLoadedBlobs));
        OnPropertyChanged(nameof(CanClearSelectedBlobs));
        OnPropertyChanged(nameof(CanDownloadSelectedBlobs));
    }

    private async Task SwitchAccountAsync(StorageAccountItemViewModel account)
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            await ResetRefreshTokenAsync();

            if (_isDisposed)
            {
                return;
            }

            IsRefreshing = true;
            ErrorMessage = null;
            BlobListErrorMessage = null;
            BlobDetailErrorMessage = null;
            await Task.Yield();

            await LoadSelectedAccountAsync(
                account,
                requestedContainerName: null,
                requestedPrefix: null,
                requestedBlobName: null,
                recordRecent: false,
                _refreshCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void RebuildAccounts()
    {
        Accounts.Clear();

        if (_appState.UseDemoData)
        {
            Accounts.Add(new StorageAccountItemViewModel(_demoStorageClient.Config, isDemo: true));
            return;
        }

        foreach (var account in _appState.Config.StorageAccounts.OrderBy(static account =>
                     string.IsNullOrWhiteSpace(account.DisplayName) ? account.AccountName : account.DisplayName,
                     StringComparer.OrdinalIgnoreCase))
        {
            Accounts.Add(new StorageAccountItemViewModel(account, isDemo: false));
        }
    }

    private StorageAccountItemViewModel ResolvePreferredAccount(string? preferredAccountId)
    {
        if (!string.IsNullOrWhiteSpace(preferredAccountId))
        {
            var matching = Accounts.FirstOrDefault(account => string.Equals(account.Id, preferredAccountId, StringComparison.Ordinal));
            if (matching is not null)
            {
                return matching;
            }
        }

        return Accounts[0];
    }

    private void SetSelectedAccount(StorageAccountItemViewModel account)
    {
        _suppressAccountSelectionSideEffects = true;
        try
        {
            SelectedAccount = account;
        }
        finally
        {
            _suppressAccountSelectionSideEffects = false;
        }
    }

    private async Task LoadSelectedAccountAsync(
        StorageAccountItemViewModel account,
        string? requestedContainerName,
        string? requestedPrefix,
        string? requestedBlobName,
        bool recordRecent,
        CancellationToken ct)
    {
        ClearSelectionState();
        _client = null;
        ConnectionSummary = account.IsDemo
            ? $"Browsing demo storage account '{account.AccountName}'."
            : $"Connecting to storage account '{account.AccountName}'.";

        try
        {
            _client = account.IsDemo ? _demoStorageClient : _storageClientFactory.Create(account.Config);
            IsLoadingContainers = true;
            await Task.Yield();

            var containers = await _client.ListContainersAsync(ct);
            ApplyContainers(containers);

            ConnectionSummary = account.IsDemo
                ? $"Browsing demo storage account '{account.AccountName}'."
                : $"Connected to storage account '{account.AccountName}'.";

            if (!string.IsNullOrWhiteSpace(requestedContainerName))
            {
                await SelectContainerCoreAsync(
                    requestedContainerName,
                    requestedPrefix ?? string.Empty,
                    requestedBlobName,
                    recordRecent,
                    ct);
                return;
            }

            await PublishSnapshotAsync(recordRecent);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage account load failed for {AccountName}.", account.AccountName);
            _client = null;
            ErrorMessage = ex.Message;
            ConnectionSummary = $"Storage account '{account.AccountName}' could not be opened.";
            Containers.Clear();
            _workspaceService.ClearCurrentSnapshot("storage");
        }
        finally
        {
            IsLoadingContainers = false;
        }
    }

    private void ApplyContainers(IReadOnlyList<StorageContainerItem> containers)
    {
        Containers.Clear();

        foreach (var container in containers
                     .OrderBy(static container => container.Name, StringComparer.OrdinalIgnoreCase))
        {
            Containers.Add(new StorageContainerItemViewModel(container));
        }
    }

    private async Task SelectContainerCoreAsync(
        string? containerName,
        string? requestedPrefix,
        string? requestedBlobName,
        bool recordRecent,
        CancellationToken ct)
    {
        if (_client is null || string.IsNullOrWhiteSpace(containerName))
        {
            return;
        }

        var container = Containers.FirstOrDefault(item => string.Equals(item.Name, containerName, StringComparison.Ordinal));
        if (container is null)
        {
            BlobListErrorMessage = $"Container '{containerName}' was not found in the selected account.";
            return;
        }

        MarkSelectedContainer(container.Name);
        CurrentPrefix = NormalizePrefix(requestedPrefix);
        CurrentFolderLabel = BuildFolderLabel(CurrentPrefix);
        ClearBlobDetailState();
        await LoadBlobPageAsync(continuationToken: null, append: false, ct);

        if (!string.IsNullOrWhiteSpace(requestedBlobName))
        {
            var selected = await EnsureBlobLoadedAndSelectedAsync(requestedBlobName, recordRecent, ct);
            if (selected)
            {
                return;
            }
        }

        await PublishSnapshotAsync(recordRecent);
    }

    private async Task LoadBlobPageAsync(string? continuationToken, bool append, CancellationToken ct)
    {
        if (_client is null || SelectedContainer is null)
        {
            return;
        }

        IsLoadingBlobs = true;
        BlobListErrorMessage = null;

        if (!append)
        {
            ResetBulkSelectionState(exitSelectionMode: true);
            Blobs.Clear();
            ContinuationToken = null;
            BuildBreadcrumbs(CurrentPrefix);
        }

        try
        {
            var page = await _client.ListBlobsAsync(
                SelectedContainer.Name,
                CurrentPrefix,
                continuationToken,
                pageSize: 100,
                ct: ct);

            foreach (var item in page.Items
                         .OrderBy(static item => item.IsPrefix ? 0 : 1)
                         .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                Blobs.Add(new StorageBlobEntryViewModel(item));
            }

            ContinuationToken = page.ContinuationToken;
            MarkSelectedBlob(SelectedBlob?.FullName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage blob list load failed for container {ContainerName} and prefix {Prefix}.", SelectedContainer.Name, CurrentPrefix);
            BlobListErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingBlobs = false;
        }
    }

    private async Task<bool> EnsureBlobLoadedAndSelectedAsync(string blobName, bool recordRecent, CancellationToken ct)
    {
        var match = FindBlob(blobName);
        while (match is null && _client is not null && SelectedContainer is not null && !string.IsNullOrWhiteSpace(ContinuationToken))
        {
            await LoadBlobPageAsync(ContinuationToken, append: true, ct);
            match = FindBlob(blobName);
        }

        if (match is null)
        {
            return false;
        }

        await SelectBlobCoreAsync(match.FullName, recordRecent, ct);
        return true;
    }

    private StorageBlobEntryViewModel? FindBlob(string blobName) =>
        Blobs.FirstOrDefault(blob => !blob.IsPrefix && string.Equals(blob.FullName, blobName, StringComparison.Ordinal));

    private async Task SelectBlobCoreAsync(string blobName, bool recordRecent, CancellationToken ct)
    {
        if (_client is null || SelectedContainer is null)
        {
            return;
        }

        var blob = FindBlob(blobName);
        if (blob is null)
        {
            BlobDetailErrorMessage = $"Blob '{blobName}' is not loaded in the current folder view.";
            return;
        }

        _selectedBlobProperties = null;
        MarkSelectedBlob(blob.FullName);
        NotifyMutationActionStateChanged();
        BlobDetailErrorMessage = null;
        IsLoadingBlobDetail = true;
        BlobDetailStatusText = "Loading blob properties and preview...";
        PrepareVersionStateForSelection();
        await Task.Yield();

        try
        {
            var propertiesTask = _client.GetBlobPropertiesAsync(SelectedContainer.Name, blob.FullName, ct);
            var contentTask = _client.GetBlobContentAsync(SelectedContainer.Name, blob.FullName, InitialPreviewByteLimit, ct);

            await Task.WhenAll(propertiesTask, contentTask);

            ApplyProperties(propertiesTask.Result);
            ApplyPreview(contentTask.Result, blob.FullName, fullPreviewRequested: false);

            SelectedBlobTitle = blob.DisplayName;
            SelectedBlobSubtitle = $"{blob.SizeText} · {blob.ContentTypeText} · {blob.LastModifiedText}";
            SelectedBlobUrl = BuildBlobUrl(SelectedAccount?.AccountName, SelectedContainer.Name, blob.FullName);
            BlobDetailStatusText = "Blob detail loaded.";

            await LoadVersionStateAsync(blob.FullName, ct);

            await PublishSnapshotAsync(recordRecent);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage blob detail load failed for {BlobName}.", blob.FullName);
            BlobDetailErrorMessage = ex.Message;
            BlobDetailStatusText = "Blob detail load failed.";
        }
        finally
        {
            IsLoadingBlobDetail = false;
        }
    }

    private void ApplyProperties(BlobProperties properties)
    {
        _selectedBlobProperties = properties;
        NotifyMutationActionStateChanged();
        BlobPropertyRows.Clear();
        MetadataRows.Clear();
        TagRows.Clear();

        AddPropertyRow("Name", properties.Name);
        AddPropertyRow("Size", StorageDisplayFormatter.FormatBytes(properties.SizeBytes));
        AddPropertyRow("Content type", string.IsNullOrWhiteSpace(properties.ContentType) ? "Unknown" : properties.ContentType);
        AddPropertyRow("Last modified", properties.LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        AddPropertyRow("ETag", properties.ETag);
        AddPropertyRow("Lease status", string.IsNullOrWhiteSpace(properties.LeaseStatus) ? "None" : properties.LeaseStatus);
        AddPropertyRow("Lease state", string.IsNullOrWhiteSpace(properties.LeaseState) ? "None" : properties.LeaseState);
        AddPropertyRow("Access tier", string.IsNullOrWhiteSpace(properties.AccessTier) ? "Not set" : properties.AccessTier);
        AddPropertyRow("Content encoding", string.IsNullOrWhiteSpace(properties.ContentEncoding) ? "None" : properties.ContentEncoding);
        AddPropertyRow("Content language", string.IsNullOrWhiteSpace(properties.ContentLanguage) ? "None" : properties.ContentLanguage);
        AddPropertyRow("Cache control", string.IsNullOrWhiteSpace(properties.CacheControl) ? "None" : properties.CacheControl);

        foreach (var row in properties.Metadata.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            MetadataRows.Add(new StorageKeyValueRowViewModel(row.Key, row.Value));
        }

        foreach (var row in properties.Tags.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            TagRows.Add(new StorageKeyValueRowViewModel(row.Key, row.Value));
        }
    }

    private void ApplyPreview(StorageBlobContent content, string blobName, bool fullPreviewRequested)
    {
        PreviewIsBinary = content.IsBinary;
        PreviewContent = content.IsBinary ? null : FormatPreviewContent(content);

        if (content.IsBinary)
        {
            PreviewInfoMessage = null;
            CanLoadExpandedPreview = false;
            return;
        }

        if (!content.WasTruncated)
        {
            PreviewInfoMessage = null;
            CanLoadExpandedPreview = false;
            return;
        }

        if (content.TotalSizeBytes <= ExpandedPreviewByteLimit && !fullPreviewRequested)
        {
            PreviewInfoMessage = "Preview limited to 512 KB. Load the full text-friendly preview to inspect the entire blob inline.";
            CanLoadExpandedPreview = true;
            return;
        }

        if (content.TotalSizeBytes > ExpandedPreviewByteLimit)
        {
            PreviewInfoMessage = fullPreviewRequested
                ? "Preview is capped at 2 MB for inline rendering. Download the blob for the full file."
                : "Preview limited to 512 KB because this blob exceeds the 2 MB inline preview cap. Download the blob for the full file.";
            CanLoadExpandedPreview = false;
            return;
        }

        PreviewInfoMessage = null;
        CanLoadExpandedPreview = false;
    }

    private async Task LoadVersionStateAsync(string blobName, CancellationToken ct)
    {
        if (_client is null || SelectedContainer is null || !IsCurrentSelectedBlob(blobName))
        {
            return;
        }

        try
        {
            IsLoadingVersions = true;
            VersionErrorMessage = null;
            VersionStatusText = "Loading version history...";
            await Task.Yield();

            _storageCapabilities = await TryGetStorageCapabilitiesAsync(ct);
            NotifyVersionCapabilityStateChanged();

            var versions = await _client.ListBlobVersionsAsync(SelectedContainer.Name, blobName, ct);

            if (!IsCurrentSelectedBlob(blobName))
            {
                return;
            }

            ApplyVersions(versions);
            VersionStatusText = versions.Count switch
            {
                0 => "No saved versions were returned for this blob.",
                1 => "1 version is available for this blob.",
                _ => $"{versions.Count} versions are available for this blob.",
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage version history load failed for {BlobName}.", blobName);

            if (!IsCurrentSelectedBlob(blobName))
            {
                return;
            }

            BlobVersions.Clear();
            VersionErrorMessage = ex.Message;
            VersionStatusText = "Version history could not be loaded.";
        }
        finally
        {
            if (IsCurrentSelectedBlob(blobName))
            {
                IsLoadingVersions = false;
            }
        }
    }

    private async Task DownloadBlobCoreAsync(
        string blobName,
        string displayName,
        string? versionId = null,
        long? totalBytes = null,
        string? progressLabel = null)
    {
        if (_client is null || SelectedContainer is null)
        {
            return;
        }

        string? destinationPath = null;

        try
        {
            await ResetDownloadTokenAsync();
            var downloadToken = _downloadCts.Token;

            destinationPath = BuildDownloadPath(displayName, versionId);
            var resolvedTotalBytes = totalBytes ?? FindBlob(blobName)?.SizeBytes;

            ActiveDownloadLabel = progressLabel ?? (versionId is null
                ? $"Downloading {displayName}"
                : $"Downloading {displayName} ({ShortVersionId(versionId)})");
            DownloadProgressPercent = 0;
            DownloadProgressText = resolvedTotalBytes is > 0
                ? $"0 of {StorageDisplayFormatter.FormatBytes(resolvedTotalBytes.Value)}"
                : "Starting download...";
            IsDownloadIndeterminate = resolvedTotalBytes is null or <= 0;
            IsDownloadingBlob = true;

            var progress = new Progress<long>(bytesTransferred =>
            {
                if (resolvedTotalBytes is > 0)
                {
                    DownloadProgressPercent = Math.Min(100, bytesTransferred * 100d / resolvedTotalBytes.Value);
                    DownloadProgressText = $"{StorageDisplayFormatter.FormatBytes(bytesTransferred)} of {StorageDisplayFormatter.FormatBytes(resolvedTotalBytes.Value)}";
                    IsDownloadIndeterminate = false;
                }
                else
                {
                    DownloadProgressText = $"{StorageDisplayFormatter.FormatBytes(bytesTransferred)} transferred";
                    IsDownloadIndeterminate = true;
                }
            });

            await using var destination = File.Create(destinationPath);
            await _client.DownloadBlobAsync(SelectedContainer.Name, blobName, destination, progress, versionId: versionId, ct: downloadToken);

            BlobDetailStatusText = versionId is null
                ? $"Downloaded to {destinationPath}."
                : $"Downloaded version to {destinationPath}.";
            _notifications.ShowSuccess(versionId is null ? "Blob downloaded" : "Blob version downloaded", destinationPath);
        }
        catch (OperationCanceledException)
        {
            BlobDetailStatusText = "Blob download cancelled.";
            DeletePartialDownload(destinationPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage blob download failed for {BlobName}.", blobName);
            BlobDetailStatusText = ex.Message;
            DeletePartialDownload(destinationPath);
            _notifications.ShowError(versionId is null ? "Blob download failed" : "Blob version download failed", ex: ex);
        }
        finally
        {
            IsDownloadingBlob = false;
        }
    }

    private async Task CopyToClipboardAsync(Func<string?> valueFactory, string successMessage)
    {
        try
        {
            var value = valueFactory();
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            StorageClipboardHelper.CopyText(value);
            _notifications.ShowSuccess(successMessage, SelectedBlob?.DisplayName);
            BlobDetailStatusText = successMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage clipboard copy failed.");
            _notifications.ShowError("Clipboard copy failed", ex: ex);
            BlobDetailStatusText = ex.Message;
        }

        await Task.CompletedTask;
    }

    private async Task PublishSnapshotAsync(bool recordRecent)
    {
        var snapshot = BuildSnapshot();
        if (snapshot is null)
        {
            _workspaceService.ClearCurrentSnapshot("storage");
            return;
        }

        await _workspaceService.PublishSnapshotAsync(snapshot, recordRecent);
    }

    private WorkspaceSnapshot? BuildSnapshot()
    {
        if (SelectedAccount is null)
        {
            return null;
        }

        var resource = new OperatorResourceReference
        {
            Key = $"storage:{SelectedAccount.Id}",
            Area = "storage",
            Kind = "account",
            DisplayName = SelectedAccount.DisplayName,
            DisplayPath = SelectedAccount.DisplayName,
            Summary = SelectedAccount.AccountName,
            Icon = "📁",
        };

        var restoreState = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["accountId"] = SelectedAccount.Id,
        };

        if (SelectedContainer is not null)
        {
            resource.Key = $"storage:{SelectedAccount.Id}:{SelectedContainer.Name}";
            resource.Kind = "container";
            resource.DisplayName = SelectedContainer.Name;
            resource.DisplayPath = $"{SelectedAccount.DisplayName}/{SelectedContainer.Name}";
            resource.Icon = "📁";
            restoreState["containerName"] = SelectedContainer.Name;

            if (!string.IsNullOrWhiteSpace(CurrentPrefix))
            {
                restoreState["prefix"] = CurrentPrefix;
            }
        }

        if (SelectedBlob is not null && SelectedContainer is not null)
        {
            resource.Key = $"storage:{SelectedAccount.Id}:{SelectedContainer.Name}:{SelectedBlob.FullName}";
            resource.Kind = "blob";
            resource.DisplayName = SelectedBlob.DisplayName;
            resource.DisplayPath = $"{SelectedAccount.DisplayName}/{SelectedContainer.Name}/{SelectedBlob.FullName}";
            resource.Icon = "📄";
            restoreState["blobName"] = SelectedBlob.FullName;
            restoreState["prefix"] = GetBlobPrefix(SelectedBlob.FullName);
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

        if (!HasAccounts)
        {
            RebuildAccounts();
        }

        if (!HasAccounts)
        {
            return;
        }

        var accountId = snapshot.RestoreState.TryGetValue("accountId", out var restoredAccountId)
            ? restoredAccountId
            : SelectedAccount?.Id;
        var containerName = snapshot.RestoreState.TryGetValue("containerName", out var restoredContainer)
            ? restoredContainer
            : null;
        var blobName = snapshot.RestoreState.TryGetValue("blobName", out var restoredBlob)
            ? restoredBlob
            : null;
        var prefix = snapshot.RestoreState.TryGetValue("prefix", out var restoredPrefix)
            ? restoredPrefix
            : blobName is null ? null : GetBlobPrefix(blobName);

        var account = Accounts.FirstOrDefault(candidate => string.Equals(candidate.Id, accountId, StringComparison.Ordinal))
                      ?? Accounts[0];

        SetSelectedAccount(account);
        await ResetRefreshTokenAsync();
        await LoadSelectedAccountAsync(account, containerName, prefix, blobName, recordRecent: false, _refreshCts.Token);
    }

    private void ClearAllState()
    {
        _client = null;
        ClearSelectionState();
        Accounts.Clear();
    }

    private void ClearSelectionState()
    {
        Containers.Clear();
        Blobs.Clear();
        Breadcrumbs.Clear();
        ResetBulkSelectionState(exitSelectionMode: true);
        ClearBlobDetailState();
        SelectedContainer = null;
        CurrentPrefix = string.Empty;
        CurrentFolderLabel = "Root";
        ContinuationToken = null;
        MarkSelectedContainer(null);
    }

    private void ClearBlobDetailState()
    {
        SelectedBlob = null;
        _selectedBlobProperties = null;
        MarkSelectedBlob(null);
        BlobPropertyRows.Clear();
        MetadataRows.Clear();
        TagRows.Clear();
        BlobDetailErrorMessage = null;
        PreviewContent = null;
        PreviewInfoMessage = null;
        PreviewIsBinary = false;
        CanLoadExpandedPreview = false;
        SelectedBlobUrl = null;
        SelectedBlobTitle = "Select a blob";
        SelectedBlobSubtitle = "Preview text-friendly content and inspect blob properties from here.";
        BlobDetailStatusText = "Select a blob to inspect properties, metadata, tags, and content preview.";
    }

    private void HandleMutationFeedback(
        BlobMutationResult result,
        string successTitle,
        string successStatus,
        string failureStatusPrefix)
    {
        if (result.Success)
        {
            BlobDetailStatusText = successStatus;
            _notifications.ShowSuccess(successTitle, result.ResultBlobPath);
            return;
        }

        var failureMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? failureStatusPrefix
            : $"{failureStatusPrefix}: {result.ErrorMessage}";

        BlobDetailStatusText = failureMessage;
        _notifications.ShowError(failureStatusPrefix, detail: result.ErrorMessage);
    }

    private BlobMutationResult HandleMutationException(string notificationTitle, string statusPrefix, Exception ex)
    {
        BlobDetailStatusText = $"{statusPrefix}: {ex.Message}";
        _notifications.ShowError(notificationTitle, ex: ex);
        return new BlobMutationResult(false, ErrorMessage: ex.Message);
    }

    private void MarkSelectedContainer(string? containerName)
    {
        StorageContainerItemViewModel? selected = null;

        foreach (var container in Containers)
        {
            var isSelected = !string.IsNullOrWhiteSpace(containerName)
                && string.Equals(container.Name, containerName, StringComparison.Ordinal);
            container.IsSelected = isSelected;
            if (isSelected)
            {
                selected = container;
            }
        }

        SelectedContainer = selected;
    }

    private void MarkSelectedBlob(string? blobName)
    {
        StorageBlobEntryViewModel? selected = null;

        foreach (var blob in Blobs)
        {
            var isSelected = !blob.IsPrefix
                && !string.IsNullOrWhiteSpace(blobName)
                && string.Equals(blob.FullName, blobName, StringComparison.Ordinal);
            blob.IsSelected = isSelected;
            if (isSelected)
            {
                selected = blob;
            }
        }

        SelectedBlob = selected;
    }

    private void BuildBreadcrumbs(string prefix)
    {
        Breadcrumbs.Clear();

        var normalizedPrefix = NormalizePrefix(prefix);
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
        {
            Breadcrumbs.Add(new StorageBreadcrumbItemViewModel("Root", string.Empty, isCurrent: true, showSeparator: false));
            return;
        }

        Breadcrumbs.Add(new StorageBreadcrumbItemViewModel("Root", string.Empty, isCurrent: false, showSeparator: false));

        var parts = normalizedPrefix.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var runningPrefix = string.Empty;

        for (var index = 0; index < parts.Length; index++)
        {
            runningPrefix = string.Concat(runningPrefix, parts[index], "/");
            var isCurrent = index == parts.Length - 1;
            Breadcrumbs.Add(new StorageBreadcrumbItemViewModel(parts[index], runningPrefix, isCurrent, showSeparator: true));
        }
    }

    private void AddPropertyRow(string label, string value)
    {
        BlobPropertyRows.Add(new StoragePropertyRowViewModel(label, value));
    }

    private static string NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return string.Empty;
        }

        var trimmed = prefix.Trim();
        return trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : string.Concat(trimmed, "/");
    }

    private static string BuildFolderLabel(string prefix) =>
        string.IsNullOrWhiteSpace(prefix) ? "Root" : prefix.TrimEnd('/');

    private static string GetBlobPrefix(string blobName)
    {
        var lastSlash = blobName.LastIndexOf('/');
        return lastSlash <= -1 ? string.Empty : blobName[..(lastSlash + 1)];
    }

    private static string FormatPreviewContent(StorageBlobContent content)
    {
        var normalized = content.Content.ReplaceLineEndings("\n");
        if (string.IsNullOrWhiteSpace(content.ContentType))
        {
            return normalized;
        }

        if (content.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var jsonDocument = JsonDocument.Parse(normalized);
                return JsonSerializer.Serialize(jsonDocument.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (JsonException)
            {
                return normalized;
            }
        }

        return normalized;
    }

    private static string BuildBlobUrl(string? accountName, string containerName, string blobName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return string.Empty;
        }

        var escapedBlobPath = string.Join("/", blobName.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
        return $"https://{accountName}.blob.core.windows.net/{Uri.EscapeDataString(containerName)}/{escapedBlobPath}";
    }

    private static string BuildDownloadPath(string displayName, string? versionId = null)
    {
        var downloadsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        Directory.CreateDirectory(downloadsFolder);

        var sanitizedFileName = BuildDownloadFileName(displayName, versionId);
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

    private static string BuildDownloadFileName(string displayName, string? versionId)
    {
        var sanitizedFileName = StorageDisplayFormatter.SanitizeFileName(displayName);
        if (string.IsNullOrWhiteSpace(versionId))
        {
            return sanitizedFileName;
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sanitizedFileName);
        var extension = Path.GetExtension(sanitizedFileName);
        var sanitizedVersionId = StorageDisplayFormatter.SanitizeFileName(versionId);
        return string.Concat(fileNameWithoutExtension, "_", sanitizedVersionId, extension);
    }

    private void DeletePartialDownload(string? destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath) || !File.Exists(destinationPath))
        {
            return;
        }

        try
        {
            File.Delete(destinationPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Deleting the partial blob download failed for {Path}.", destinationPath);
        }
    }

    private async Task ResetRefreshTokenAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await CancelRefreshTokenAsync();

        if (_isDisposed)
        {
            return;
        }

        _refreshCts.Dispose();
        _refreshCts = new CancellationTokenSource();
    }

    private async Task ResetDownloadTokenAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await CancelDownloadTokenAsync();

        if (_isDisposed)
        {
            return;
        }

        _downloadCts.Dispose();
        _downloadCts = new CancellationTokenSource();
    }

    private async Task CancelRefreshTokenAsync()
    {
        if (_refreshCts.IsCancellationRequested)
        {
            return;
        }

        await CancelTokenAsync(_refreshCts);
    }

    private async Task CancelDownloadTokenAsync()
    {
        if (_downloadCts.IsCancellationRequested)
        {
            return;
        }

        await CancelTokenAsync(_downloadCts);
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

    private void ResetBulkSelectionState(bool exitSelectionMode)
    {
        _selectedBlobNames.Clear();
        if (exitSelectionMode)
        {
            IsSelectionMode = false;
        }

        RefreshBulkSelectionState();
    }

    private void RefreshBulkSelectionState()
    {
        _selectedBlobNames.RemoveWhere(blobName => Blobs.All(candidate => !string.Equals(candidate.FullName, blobName, StringComparison.Ordinal)));

        foreach (var blob in Blobs)
        {
            blob.IsSelectionMode = IsSelectionMode;
            blob.IsBatchSelected = _selectedBlobNames.Contains(blob.FullName);
        }

        BulkSelectionSummaryText = SelectedBlobCount switch
        {
            0 => "No loaded blobs selected for ZIP download.",
            1 => "1 loaded blob selected for ZIP download.",
            _ => $"{SelectedBlobCount} loaded blobs selected for ZIP download.",
        };

        OnPropertyChanged(nameof(SelectedBlobCount));
        OnPropertyChanged(nameof(SelectionModeButtonLabel));
        OnPropertyChanged(nameof(BulkToolbarVisibility));
        OnPropertyChanged(nameof(BulkSelectionSummaryVisibility));
        OnPropertyChanged(nameof(BulkSelectionToggleVisibility));
        OnPropertyChanged(nameof(CanToggleSelectionMode));
        OnPropertyChanged(nameof(CanSelectAllLoadedBlobs));
        OnPropertyChanged(nameof(CanClearSelectedBlobs));
        OnPropertyChanged(nameof(CanDownloadSelectedBlobs));
    }

    private static string BuildZipDownloadPath(string containerName)
    {
        var downloadsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        Directory.CreateDirectory(downloadsFolder);

        var zipFileName = $"{StorageDisplayFormatter.SanitizeFileName(containerName)}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        var candidatePath = Path.Combine(downloadsFolder, zipFileName);
        if (!File.Exists(candidatePath))
        {
            return candidatePath;
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(zipFileName);

        for (var index = 1; index < 1000; index++)
        {
            candidatePath = Path.Combine(downloadsFolder, $"{fileNameWithoutExtension} ({index}).zip");
            if (!File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return Path.Combine(downloadsFolder, $"{Guid.NewGuid():N}.zip");
    }

    private static string BuildZipEntryName(string blobName)
    {
        var rawSegments = blobName.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawSegments.Length == 0)
        {
            return "blob-download";
        }

        var safeSegments = rawSegments.Select(segment =>
        {
            var normalizedSegment = segment switch
            {
                "." => "_",
                ".." => "__",
                _ => segment,
            };

            return StorageDisplayFormatter.SanitizeFileName(normalizedSegment);
        });

        return string.Join('/', safeSegments);
    }

    private static string BuildUniqueZipEntryName(string blobName, ISet<string> usedEntryNames)
    {
        var entryName = BuildZipEntryName(blobName);
        if (usedEntryNames.Add(entryName))
        {
            return entryName;
        }

        var separatorIndex = entryName.LastIndexOf('/');
        var directory = separatorIndex >= 0 ? entryName[..separatorIndex] : string.Empty;
        var fileName = separatorIndex >= 0 ? entryName[(separatorIndex + 1)..] : entryName;
        var extension = Path.GetExtension(fileName);
        var baseName = fileName[..Math.Max(0, fileName.Length - extension.Length)];

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidateFileName = $"{baseName} ({suffix}){extension}";
            var candidate = string.IsNullOrWhiteSpace(directory)
                ? candidateFileName
                : $"{directory}/{candidateFileName}";

            if (usedEntryNames.Add(candidate))
            {
                return candidate;
            }
        }

        var fallback = string.IsNullOrWhiteSpace(directory)
            ? $"{baseName}-{Guid.NewGuid():N}{extension}"
            : $"{directory}/{baseName}-{Guid.NewGuid():N}{extension}";

        usedEntryNames.Add(fallback);
        return fallback;
    }

    private void PrepareVersionStateForSelection()
    {
        BlobVersions.Clear();
        _storageCapabilities = null;
        VersionErrorMessage = null;
        VersionComparisonSummary = null;
        VersionComparisonText = null;
        VersionStatusText = "Loading version history...";
        IsLoadingVersions = false;
        IsLoadingVersionComparison = false;
        NotifyVersionCapabilityStateChanged();
    }

    private void ApplyVersions(IReadOnlyList<BlobVersionItem> versions)
    {
        BlobVersions.Clear();

        foreach (var version in versions
                     .OrderByDescending(static version => version.IsCurrentVersion)
                     .ThenByDescending(static version => version.CreatedOn))
        {
            BlobVersions.Add(new StorageBlobVersionViewModel(version));
        }
    }

    private async Task<StorageCapabilities?> TryGetStorageCapabilitiesAsync(CancellationToken ct)
    {
        if (_client is null)
        {
            return null;
        }

        try
        {
            return await _client.GetStorageCapabilitiesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Storage capability probe failed.");
            return null;
        }
    }

    private async Task RefreshSelectedBlobDetailAsync(string blobName, CancellationToken ct)
    {
        if (!IsCurrentSelectedBlob(blobName))
        {
            return;
        }

        await SelectBlobCoreAsync(blobName, recordRecent: false, ct);
    }

    private bool IsCurrentSelectedBlob(string blobName) =>
        SelectedBlob is not null && string.Equals(SelectedBlob.FullName, blobName, StringComparison.Ordinal);

    private void NotifyVersionCapabilityStateChanged()
    {
        OnPropertyChanged(nameof(CanRestoreVersions));
        OnPropertyChanged(nameof(RestoreVersionActionVisibility));
    }

    private void NotifyMutationActionStateChanged()
    {
        OnPropertyChanged(nameof(CanUploadToSelectedContainer));
        OnPropertyChanged(nameof(CanCopySelectedBlob));
        OnPropertyChanged(nameof(CanEditSelectedBlobMetadata));
        OnPropertyChanged(nameof(CanCopySelectedBlobPath));
        OnPropertyChanged(nameof(UploadActionVisibility));
        OnPropertyChanged(nameof(ContainerSasActionVisibility));
        OnPropertyChanged(nameof(CopyBlobActionVisibility));
        OnPropertyChanged(nameof(EditMetadataActionVisibility));
        OnPropertyChanged(nameof(CopyBlobPathActionVisibility));
    }

    private void NotifyPreviewActionStateChanged()
    {
        OnPropertyChanged(nameof(CanCopyPreviewContent));
        OnPropertyChanged(nameof(CopyPreviewActionVisibility));
    }

    private static string BuildVersionComparisonSummary(BlobVersionComparison comparison)
    {
        var parts = new List<string>
        {
            $"Base {ShortVersionId(comparison.BaseVersionId)}",
        };

        if (!string.IsNullOrWhiteSpace(comparison.CompareVersionId))
        {
            parts.Add($"Compare {ShortVersionId(comparison.CompareVersionId)}");
        }

        parts.Add($"Sizes {FormatOptionalBytes(comparison.BaseSizeBytes)} -> {FormatOptionalBytes(comparison.CompareSizeBytes)}");
        parts.Add($"Metadata +{comparison.MetadataDiff.AddedKeys.Count} / -{comparison.MetadataDiff.RemovedKeys.Count} / ~{comparison.MetadataDiff.ChangedKeys.Count}");
        parts.Add(comparison.ContentComparePossible ? "Text diff available." : "Text diff unavailable.");
        return string.Join(" · ", parts);
    }

    private static string FormatOptionalBytes(long? sizeBytes) =>
        sizeBytes is > 0 ? StorageDisplayFormatter.FormatBytes(sizeBytes.Value) : "n/a";

    private static string ShortVersionId(string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId))
        {
            return "current blob";
        }

        return versionId.Length <= 16 ? versionId : versionId[..16];
    }
}

public sealed partial class StorageAccountItemViewModel : ObservableObject
{
    public StorageAccountItemViewModel(StorageConfig config, bool isDemo)
    {
        Config = config;
        IsDemo = isDemo;
    }

    public StorageConfig Config { get; }

    public bool IsDemo { get; }

    public string Id => Config.Id;

    public string DisplayName => string.IsNullOrWhiteSpace(Config.DisplayName) ? Config.AccountName : Config.DisplayName;

    public string AccountName => Config.AccountName;

    public string DisplayLabel => IsDemo ? $"{DisplayName} ({AccountName}, demo)" : $"{DisplayName} ({AccountName})";
}

public sealed partial class StorageContainerItemViewModel : ObservableObject
{
    public StorageContainerItemViewModel(StorageContainerItem container)
    {
        Name = container.Name;
        Subtitle = BuildSubtitle(container);
    }

    public string Name { get; }

    public string Subtitle { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string SelectionStateLabel => IsSelected ? "Selected" : string.Empty;

    public Visibility SelectionStateVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectionStateLabel));
        OnPropertyChanged(nameof(SelectionStateVisibility));
    }

    private static string BuildSubtitle(StorageContainerItem container)
    {
        var parts = new List<string>();
        if (container.LastModified is not null)
        {
            parts.Add($"Updated {container.LastModified.Value.ToLocalTime():yyyy-MM-dd HH:mm}");
        }

        if (!string.IsNullOrWhiteSpace(container.PublicAccess))
        {
            parts.Add($"Access: {container.PublicAccess}");
        }

        if (!string.IsNullOrWhiteSpace(container.LeaseStatus))
        {
            parts.Add($"Lease: {container.LeaseStatus}");
        }

        return parts.Count == 0 ? "Ready to browse." : string.Join(" · ", parts);
    }
}

public sealed partial class StorageBlobEntryViewModel : ObservableObject
{
    public StorageBlobEntryViewModel(StorageBlobItem blob)
    {
        FullName = blob.Name;
        IsPrefix = blob.IsPrefix;
        DisplayName = ResolveDisplayName(blob.Name, blob.IsPrefix);
        SizeBytes = blob.SizeBytes;
        SizeText = blob.IsPrefix ? "Folder" : StorageDisplayFormatter.FormatBytes(blob.SizeBytes ?? 0);
        ContentTypeText = blob.IsPrefix
            ? "Virtual folder"
            : string.IsNullOrWhiteSpace(blob.ContentType) ? "Unknown content type" : blob.ContentType;
        LastModifiedText = blob.LastModified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "No timestamp";
        DetailLine = blob.IsPrefix
            ? "Browse the next folder level."
            : $"{SizeText} · {ContentTypeText} · {LastModifiedText}";
        PrimaryActionLabel = blob.IsPrefix ? "Open" : "Preview";
        DownloadVisibility = blob.IsPrefix ? Visibility.Collapsed : Visibility.Visible;
    }

    public string FullName { get; }

    public bool IsPrefix { get; }

    public long? SizeBytes { get; }

    public string DisplayName { get; }

    public string SizeText { get; }

    public string ContentTypeText { get; }

    public string LastModifiedText { get; }

    public string DetailLine { get; }

    public string PrimaryActionLabel { get; }

    public Visibility DownloadVisibility { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsSelectionMode { get; set; }

    [ObservableProperty]
    public partial bool IsBatchSelected { get; set; }

    public string SelectionStateLabel => IsSelected ? "Selected" : string.Empty;

    public Visibility SelectionStateVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SelectionToggleVisibility => IsSelectionMode && !IsPrefix ? Visibility.Visible : Visibility.Collapsed;

    public string SelectionActionLabel => IsBatchSelected ? "Selected" : "Select";

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectionStateLabel));
        OnPropertyChanged(nameof(SelectionStateVisibility));
    }

    partial void OnIsSelectionModeChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectionToggleVisibility));
    }

    partial void OnIsBatchSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectionActionLabel));
    }

    private static string ResolveDisplayName(string blobName, bool isPrefix)
    {
        var trimmed = isPrefix ? blobName.TrimEnd('/') : blobName;
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
    }
}

public sealed class StorageBlobVersionViewModel
{
    public StorageBlobVersionViewModel(BlobVersionItem version)
    {
        VersionId = version.VersionId;
        ShortVersionId = version.VersionId.Length <= 16 ? version.VersionId : version.VersionId[..16];
        CreatedOnText = version.CreatedOn?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Unknown timestamp";
        ContentLength = version.ContentLength;
        SizeText = version.ContentLength is > 0 ? StorageDisplayFormatter.FormatBytes(version.ContentLength.Value) : "Unknown size";
        IsCurrentVersion = version.IsCurrentVersion;
        CurrentVersionVisibility = version.IsCurrentVersion ? Visibility.Visible : Visibility.Collapsed;
        SummaryText = $"{CreatedOnText} · {SizeText}";
    }

    public string VersionId { get; }

    public string ShortVersionId { get; }

    public string CreatedOnText { get; }

    public long? ContentLength { get; }

    public string SizeText { get; }

    public bool IsCurrentVersion { get; }

    public Visibility CurrentVersionVisibility { get; }

    public string SummaryText { get; }
}

public sealed class StorageBreadcrumbItemViewModel
{
    public StorageBreadcrumbItemViewModel(string label, string prefix, bool isCurrent, bool showSeparator)
    {
        Label = label;
        Prefix = prefix;
        IsCurrent = isCurrent;
        ShowSeparator = showSeparator;
    }

    public string Label { get; }

    public string Prefix { get; }

    public bool IsCurrent { get; }

    public bool ShowSeparator { get; }

    public Visibility ButtonVisibility => IsCurrent ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CurrentTextVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SeparatorVisibility => ShowSeparator ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class StoragePropertyRowViewModel(string label, string value)
{
    public string Label { get; } = label;

    public string Value { get; } = value;
}

public sealed class StorageKeyValueRowViewModel(string key, string value)
{
    public string Key { get; } = key;

    public string Value { get; } = value;
}

internal static class StorageDisplayFormatter
{
    private static readonly HashSet<string> ReservedWindowsDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string FormatBytes(long sizeBytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = sizeBytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{value:0} {units[unitIndex]}" : $"{value:0.##} {units[unitIndex]}";
    }

    public static string SanitizeFileName(string fileName)
    {
        var fallback = string.IsNullOrWhiteSpace(fileName) ? "blob-download" : fileName;
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitizedCharacters = fallback.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray();
        var sanitized = new string(sanitizedCharacters).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "blob-download";
        }

        var extension = Path.GetExtension(sanitized);
        var stem = Path.GetFileNameWithoutExtension(sanitized);
        if (ReservedWindowsDeviceNames.Contains(stem))
        {
            sanitized = $"_{stem}{extension}";
        }

        return sanitized;
    }
}

internal static class StorageClipboardHelper
{
    public static void CopyText(string value)
    {
        var package = new DataPackage();
        package.SetText(value);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}