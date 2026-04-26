using System.Reflection;
using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using SwebKit.WinUI.Services;
using SwebKit.WinUI.ViewModels.Storage;

namespace SwebKit.WinUI.Tests;

public sealed class StoragePageViewModelTests
{
    [Fact]
    public async Task OpenBlobEntry_LoadsBlobVersionsAndCapabilities()
    {
        var harness = CreateHarness();

        await LoadBlobAsync(harness.ViewModel);

        Assert.Equal(1, harness.Client.ListBlobVersionsCallCount);
        Assert.Equal(2, harness.ViewModel.BlobVersions.Count);
        Assert.True(harness.ViewModel.CanRestoreVersions);
        Assert.Contains("versions are available", harness.ViewModel.VersionStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompareVersion_PopulatesComparisonSummaryAndText()
    {
        var harness = CreateHarness();

        await LoadBlobAsync(harness.ViewModel);
        var version = harness.ViewModel.BlobVersions.Last();

        await harness.ViewModel.CompareVersionCommand.ExecuteAsync(version);

        Assert.Equal(version.VersionId, harness.Client.LastComparedVersionId);
        Assert.NotNull(harness.ViewModel.VersionComparisonSummary);
        Assert.Contains("Metadata", harness.ViewModel.VersionComparisonSummary, StringComparison.Ordinal);
        Assert.NotNull(harness.ViewModel.VersionComparisonText);
        Assert.Contains("+ value: 2", harness.ViewModel.VersionComparisonText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenBlobEntry_LoadsVersionsWhenCapabilityProbeCannotConfirmVersioning()
    {
        var harness = CreateHarness();
        harness.Client.Capabilities = new StorageCapabilities(
            VersioningEnabled: false,
            SoftDeleteEnabled: true,
            CanUpload: true,
            CanCopy: true,
            CanSetMetadata: true,
            CanRestore: true);

        await LoadBlobAsync(harness.ViewModel);

        Assert.Equal(2, harness.ViewModel.BlobVersions.Count);
        Assert.True(harness.ViewModel.CanRestoreVersions);
        Assert.Contains("versions are available", harness.ViewModel.VersionStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenBlobEntry_ReadOnlyConfig_DoesNotExposeRestore()
    {
        var harness = CreateHarness(allowMutations: false);

        await LoadBlobAsync(harness.ViewModel);

        Assert.Equal(2, harness.ViewModel.BlobVersions.Count);
        Assert.False(harness.ViewModel.AllowMutations);
        Assert.False(harness.ViewModel.CanRestoreVersions);
    }

    [Fact]
    public async Task RestoreVersion_InvokesRecoveryApi()
    {
        var harness = CreateHarness();

        await LoadBlobAsync(harness.ViewModel);
        var version = harness.ViewModel.BlobVersions.Last();

        await harness.ViewModel.RestoreVersionCommand.ExecuteAsync(version);

        Assert.Equal(version.VersionId, harness.Client.LastRestoredVersionId);
        Assert.Contains("Restored version", harness.ViewModel.VersionStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadVersion_UsesRequestedVersionIdAndWritesVersionFile()
    {
        var harness = CreateHarness();
        string? downloadedPath = null;

        try
        {
            await LoadBlobAsync(harness.ViewModel);
            var version = harness.ViewModel.BlobVersions.Last();

            await harness.ViewModel.DownloadVersionCommand.ExecuteAsync(version);

            downloadedPath = harness.Client.LastDownloadPath;
            Assert.Equal(version.VersionId, harness.Client.LastDownloadedVersionId);
            Assert.False(string.IsNullOrWhiteSpace(downloadedPath));
            Assert.True(File.Exists(downloadedPath));
            Assert.Contains(SanitizeFileNameSegment(version.VersionId), Path.GetFileName(downloadedPath), StringComparison.Ordinal);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(downloadedPath) && File.Exists(downloadedPath))
            {
                File.Delete(downloadedPath);
            }
        }
    }

    [Fact]
    public async Task DownloadSelectedBlobsZip_DownloadsLoadedSelectionAndClearsSelection()
    {
        var harness = CreateHarness();
        string? zipPath = null;

        try
        {
            await LoadContainerAsync(harness.ViewModel);

            harness.ViewModel.ToggleSelectionModeCommand.Execute(null);
            harness.ViewModel.SelectAllLoadedBlobsCommand.Execute(null);

            Assert.Equal(2, harness.ViewModel.SelectedBlobCount);

            await harness.ViewModel.DownloadSelectedBlobsZipCommand.ExecuteAsync(null);

            Assert.Contains("Downloaded 2 blob(s)", harness.ViewModel.BulkSelectionSummaryText, StringComparison.Ordinal);
            Assert.Equal(0, harness.ViewModel.SelectedBlobCount);
            Assert.Equal(2, harness.Client.DownloadedBlobNames.Count);
            Assert.Contains("app-settings.json", harness.Client.DownloadedBlobNames, StringComparer.Ordinal);
            Assert.Contains("feature-flags.json", harness.Client.DownloadedBlobNames, StringComparer.Ordinal);

            zipPath = ExtractDownloadPath(harness.ViewModel.BulkSelectionSummaryText);
            Assert.False(string.IsNullOrWhiteSpace(zipPath));
            Assert.True(File.Exists(zipPath));

            using var archive = ZipFile.OpenRead(zipPath);
            Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "app-settings.json", StringComparison.Ordinal));
            Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "feature-flags.json", StringComparison.Ordinal));
            Assert.Contains(harness.Notifications.All, notification => notification.Severity == NotificationSeverity.Success && string.Equals(notification.Message, "ZIP downloaded", StringComparison.Ordinal));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(zipPath) && File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
        }
    }

    [Fact]
    public async Task DownloadSelectedBlobsZip_NormalizesAndDeduplicatesBlobNames()
    {
        var harness = CreateHarness();
        harness.Client.Blobs =
        [
            new StorageBlobItem("app-settings.json", false, 512, "application/json", DateTimeOffset.Parse("2026-04-24T10:00:00Z"), "\"etag-1\""),
            new StorageBlobItem("logs/2026/feature-flags.json", false, 256, "application/json", DateTimeOffset.Parse("2026-04-24T11:00:00Z"), "\"etag-2\""),
            new StorageBlobItem("reports/../escape.txt", false, 128, "text/plain", DateTimeOffset.Parse("2026-04-24T12:00:00Z"), "\"etag-3\""),
            new StorageBlobItem("reports/__/escape.txt", false, 128, "text/plain", DateTimeOffset.Parse("2026-04-24T12:05:00Z"), "\"etag-4\""),
            new StorageBlobItem("device/CON.txt", false, 64, "text/plain", DateTimeOffset.Parse("2026-04-24T12:10:00Z"), "\"etag-5\"")
        ];

        string? zipPath = null;

        try
        {
            await LoadContainerAsync(harness.ViewModel);

            harness.ViewModel.ToggleSelectionModeCommand.Execute(null);
            harness.ViewModel.SelectAllLoadedBlobsCommand.Execute(null);

            await harness.ViewModel.DownloadSelectedBlobsZipCommand.ExecuteAsync(null);

            zipPath = ExtractDownloadPath(harness.ViewModel.BulkSelectionSummaryText);
            Assert.False(string.IsNullOrWhiteSpace(zipPath));
            Assert.True(File.Exists(zipPath));

            using var archive = ZipFile.OpenRead(zipPath);
            Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "app-settings.json", StringComparison.Ordinal));
            Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "device/_CON.txt", StringComparison.Ordinal));
            Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "logs/2026/feature-flags.json", StringComparison.Ordinal));
            Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "reports/__/escape.txt", StringComparison.Ordinal));
            Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "reports/__/escape (2).txt", StringComparison.Ordinal));
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("..", StringComparison.Ordinal));
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains('\\'));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(zipPath) && File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
        }
    }

    [Fact]
    public async Task UploadBlobAsync_UsesSelectedContainerAndReportsSuccess()
    {
        var harness = CreateHarness();

        await LoadContainerAsync(harness.ViewModel);
        await using var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"uploaded\":true}"));

        var result = await harness.ViewModel.UploadBlobAsync("uploaded.json", source, overwrite: true, contentType: "application/json");

        Assert.True(result.Success);
        Assert.NotNull(harness.Client.LastUploadOptions);
        Assert.Equal("configs", harness.Client.LastUploadOptions!.ContainerName);
        Assert.Equal("uploaded.json", harness.Client.LastUploadOptions.BlobName);
        Assert.True(harness.Client.LastUploadOptions.Overwrite);
        Assert.Equal("application/json", harness.Client.LastUploadOptions.ContentType);
        Assert.Contains("Uploaded to configs/uploaded.json", harness.ViewModel.BlobDetailStatusText, StringComparison.Ordinal);
        Assert.Contains(harness.Notifications.All, notification => notification.Severity == NotificationSeverity.Success && string.Equals(notification.Message, "Blob uploaded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CopySelectedBlobAsync_UsesSelectedBlobAndDestination()
    {
        var harness = CreateHarness();

        await LoadBlobAsync(harness.ViewModel);

        var result = await harness.ViewModel.CopySelectedBlobAsync("archive", "app-settings-copy.json", overwrite: true);

        Assert.True(result.Success);
        Assert.NotNull(harness.Client.LastCopyOptions);
        Assert.Equal("configs", harness.Client.LastCopyOptions!.SourceContainer);
        Assert.Equal("app-settings.json", harness.Client.LastCopyOptions.SourceBlobName);
        Assert.Equal("archive", harness.Client.LastCopyOptions.DestinationContainer);
        Assert.Equal("app-settings-copy.json", harness.Client.LastCopyOptions.DestinationBlobName);
        Assert.True(harness.Client.LastCopyOptions.Overwrite);
        Assert.Contains("Copied to archive/app-settings-copy.json", harness.ViewModel.BlobDetailStatusText, StringComparison.Ordinal);
        Assert.Contains(harness.Notifications.All, notification => notification.Severity == NotificationSeverity.Success && string.Equals(notification.Message, "Blob copied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveSelectedBlobMetadataAsync_UsesCurrentEtagAndRefreshesMetadataRows()
    {
        var harness = CreateHarness();

        await LoadBlobAsync(harness.ViewModel);

        var result = await harness.ViewModel.SaveSelectedBlobMetadataAsync(new Dictionary<string, string>
        {
            ["owner"] = "ops",
            ["environment"] = "prod"
        });

        Assert.True(result.Success);
        Assert.Equal("\"etag-1\"", harness.Client.LastMetadataIfMatchEtag);
        Assert.NotNull(harness.Client.LastSavedMetadata);
        Assert.Equal("ops", harness.Client.LastSavedMetadata!["owner"]);
        Assert.Equal("prod", harness.Client.LastSavedMetadata["environment"]);
        Assert.Contains(harness.ViewModel.MetadataRows, row => string.Equals(row.Key, "owner", StringComparison.Ordinal) && string.Equals(row.Value, "ops", StringComparison.Ordinal));
        Assert.Contains(harness.ViewModel.MetadataRows, row => string.Equals(row.Key, "environment", StringComparison.Ordinal) && string.Equals(row.Value, "prod", StringComparison.Ordinal));
        Assert.Contains("Blob metadata updated", harness.ViewModel.BlobDetailStatusText, StringComparison.Ordinal);
        Assert.Contains(harness.Notifications.All, notification => notification.Severity == NotificationSeverity.Success && string.Equals(notification.Message, "Metadata saved", StringComparison.Ordinal));
    }

    private static async Task LoadBlobAsync(StoragePageViewModel viewModel)
    {
        await LoadContainerAsync(viewModel);

        var blob = Assert.Single(viewModel.Blobs, candidate => !candidate.IsPrefix && string.Equals(candidate.FullName, "app-settings.json", StringComparison.Ordinal));
        await viewModel.OpenBlobEntryCommand.ExecuteAsync(blob);
    }

    private static async Task LoadContainerAsync(StoragePageViewModel viewModel)
    {
        await viewModel.LoadAsync();

        var container = Assert.Single(viewModel.Containers);
        await viewModel.SelectContainerCommand.ExecuteAsync(container);
    }

    private static string ExtractDownloadPath(string message)
    {
        const string marker = " blob(s) to ";
        var markerIndex = message.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex > 0, $"Unexpected download message: {message}");
        return message[(markerIndex + marker.Length)..].TrimEnd('.');
    }

    private static StoragePageHarness CreateHarness(bool allowMutations = true)
    {
        var profileRepository = new ProfileRepository();
        var uiStateRepository = new UiStateRepository();
        var appState = new AppStateService(
            profileRepository,
            uiStateRepository,
            new AppEventBus(NullLogger<AppEventBus>.Instance));

        MarkInitialized(appState);

        appState.Config.StorageAccounts.Add(new StorageConfig
        {
            Id = "storage-primary",
            DisplayName = "Primary Storage",
            AccountName = "storageprimary",
            ConnectionStringRef = "storage-primary",
            UseAad = false,
            AllowMutations = allowMutations,
        });

        var navigation = new TestShellNavigationService();
        var workspaceService = new OperatorWorkspaceService(
            appState,
            uiStateRepository,
            navigation,
            Array.Empty<IOperatorResourceSearchProvider>());

        var client = new TestStorageClient();
        var notifications = new TestNotificationService();
        var viewModel = new StoragePageViewModel(
            appState,
            new TestStorageClientFactory(client),
            new DemoStorageClient(),
            notifications,
            workspaceService,
            navigation,
            NullLogger<StoragePageViewModel>.Instance);

        return new StoragePageHarness(viewModel, client, notifications);
    }

    private static void MarkInitialized(AppStateService appState)
    {
        var initializedField = typeof(AppStateService).GetField("<IsInitialized>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        initializedField?.SetValue(appState, true);

        var initializedTcsField = typeof(AppStateService).GetField("_initializedTcs", BindingFlags.Instance | BindingFlags.NonPublic);
        var initializedTcs = (TaskCompletionSource?)initializedTcsField?.GetValue(appState);
        initializedTcs?.TrySetResult();
    }

    private static string SanitizeFileNameSegment(string value)
    {
        var sanitized = value;
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalidChar, '_');
        }

        return sanitized;
    }

    private sealed record StoragePageHarness(StoragePageViewModel ViewModel, TestStorageClient Client, TestNotificationService Notifications);

    private sealed class TestStorageClientFactory(TestStorageClient client) : IStorageClientFactory
    {
        public IStorageClient Create(StorageConfig config) => client;
    }

    private sealed class TestStorageClient : IStorageClient
    {
        private static readonly StorageContainerItem Container = new("configs", DateTimeOffset.Parse("2026-04-20T08:00:00Z"), "None", "unlocked");
        private static readonly StorageBlobItem Blob = new("app-settings.json", false, 512, "application/json", DateTimeOffset.Parse("2026-04-24T10:00:00Z"), "\"etag-1\"");
        private static readonly StorageBlobItem SecondaryBlob = new("feature-flags.json", false, 256, "application/json", DateTimeOffset.Parse("2026-04-24T11:00:00Z"), "\"etag-2\"");
        private static readonly BlobVersionItem CurrentVersion = new("2026-04-24T10:00:00Z", DateTimeOffset.Parse("2026-04-24T10:00:00Z"), 512, true);
        private static readonly BlobVersionItem PreviousVersion = new("2026-04-18T09:30:00Z", DateTimeOffset.Parse("2026-04-18T09:30:00Z"), 480, false);

        public StorageConfig Config { get; } = new()
        {
            Id = "storage-primary",
            DisplayName = "Primary Storage",
            AccountName = "storageprimary",
            ConnectionStringRef = "storage-primary",
            UseAad = false,
            AllowMutations = true,
        };

        public int ListBlobVersionsCallCount { get; private set; }

        public string? LastComparedVersionId { get; private set; }

        public string? LastRestoredVersionId { get; private set; }

        public string? LastDownloadedVersionId { get; private set; }

        public string? LastDownloadPath { get; private set; }

        public BlobUploadOptions? LastUploadOptions { get; private set; }

        public BlobCopyOptions? LastCopyOptions { get; private set; }

        public IReadOnlyDictionary<string, string>? LastSavedMetadata { get; private set; }

        public string? LastMetadataIfMatchEtag { get; private set; }

        public List<string> DownloadedBlobNames { get; } = [];

        private Dictionary<string, string> CurrentMetadata { get; } = new(StringComparer.Ordinal)
        {
            ["environment"] = "test"
        };

        public StorageCapabilities Capabilities { get; set; } = new(
            VersioningEnabled: true,
            SoftDeleteEnabled: true,
            CanUpload: true,
            CanCopy: true,
            CanSetMetadata: true,
            CanRestore: true);

        public IReadOnlyList<StorageBlobItem> Blobs { get; set; } = [Blob, SecondaryBlob];

        public int UndeleteCallCount { get; private set; }

        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<IReadOnlyList<StorageContainerItem>> ListContainersAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StorageContainerItem>>([Container]);

        public Task<StorageBlobPage> ListBlobsAsync(string containerName, string prefix, string? continuationToken = null, int pageSize = 100, CancellationToken ct = default)
        {
            IReadOnlyList<StorageBlobItem> items = string.IsNullOrWhiteSpace(prefix) ? Blobs : [];
            return Task.FromResult(new StorageBlobPage(items, null));
        }

        public Task<BlobProperties> GetBlobPropertiesAsync(string containerName, string blobName, CancellationToken ct = default) =>
            Task.FromResult(new BlobProperties(
                blobName,
                512,
                "application/json",
                DateTimeOffset.Parse("2026-04-24T10:00:00Z"),
                "\"etag-1\"",
                "unlocked",
                "available",
                "Hot",
                false,
                null,
                null,
                null,
                new Dictionary<string, string>(CurrentMetadata, StringComparer.Ordinal),
                new Dictionary<string, string> { ["feature"] = "storage" }));

        public Task<StorageBlobContent> GetBlobContentAsync(string containerName, string blobName, int maxBytes = 524_288, CancellationToken ct = default)
        {
            const string content = "{\n  \"feature\": \"storage\",\n  \"value\": 2\n}";
            return Task.FromResult(new StorageBlobContent(containerName, blobName, content, "application/json", content.Length, false, false));
        }

        public Task<string> GetBlobSasUrlAsync(string containerName, string blobName, TimeSpan expiry, CancellationToken ct = default) =>
            Task.FromResult($"https://storageprimary.blob.core.windows.net/{containerName}/{blobName}?sv=test");

        public async Task DownloadBlobAsync(string containerName, string blobName, Stream destination, IProgress<long>? progress = null, string? versionId = null, CancellationToken ct = default)
        {
            LastDownloadedVersionId = versionId;
            LastDownloadPath = (destination as FileStream)?.Name;
            DownloadedBlobNames.Add(blobName);

            var bytes = System.Text.Encoding.UTF8.GetBytes(versionId is null ? "current-version" : $"version:{versionId}");
            await destination.WriteAsync(bytes, ct);
            progress?.Report(bytes.Length);
            await destination.FlushAsync(ct);
        }

        public Task<IReadOnlyList<BlobVersionItem>> ListBlobVersionsAsync(string containerName, string blobName, CancellationToken ct = default)
        {
            ListBlobVersionsCallCount++;
            return Task.FromResult<IReadOnlyList<BlobVersionItem>>([CurrentVersion, PreviousVersion]);
        }

        public Task<string> GetContainerSasUrlAsync(string containerName, TimeSpan expiry, CancellationToken ct = default) =>
            Task.FromResult($"https://storageprimary.blob.core.windows.net/{containerName}?sv=test");

        public Task<StorageCapabilities> GetStorageCapabilitiesAsync(CancellationToken ct = default) =>
            Task.FromResult(Capabilities);

        public Task<BlobMutationResult> UploadBlobAsync(BlobUploadOptions options, Stream source, IProgress<long>? progress = null, CancellationToken ct = default)
        {
            LastUploadOptions = options;
            return Task.FromResult(new BlobMutationResult(true, ResultBlobPath: $"{options.ContainerName}/{options.BlobName}"));
        }

        public Task<BlobMutationResult> CopyBlobAsync(BlobCopyOptions options, CancellationToken ct = default)
        {
            LastCopyOptions = options;
            return Task.FromResult(new BlobMutationResult(true, ResultBlobPath: $"{options.DestinationContainer}/{options.DestinationBlobName}"));
        }

        public Task<BlobMutationResult> SetBlobMetadataAsync(string containerName, string blobName, IDictionary<string, string> metadata, string? ifMatchEtag = null, CancellationToken ct = default)
        {
            LastMetadataIfMatchEtag = ifMatchEtag;
            LastSavedMetadata = new Dictionary<string, string>(metadata, StringComparer.Ordinal);
            CurrentMetadata.Clear();

            foreach (var pair in metadata)
            {
                CurrentMetadata[pair.Key] = pair.Value;
            }

            return Task.FromResult(new BlobMutationResult(true, ResultBlobPath: $"{containerName}/{blobName}"));
        }

        public Task<BlobVersionComparison> GetVersionComparisonAsync(string containerName, string blobName, string baseVersionId, string? compareVersionId = null, CancellationToken ct = default)
        {
            LastComparedVersionId = baseVersionId;

            var diff = new BlobMetadataDiff(
                Before: new Dictionary<string, string?> { ["feature"] = "storage", ["value"] = "1" },
                After: new Dictionary<string, string?> { ["feature"] = "storage", ["value"] = "2" },
                AddedKeys: [],
                RemovedKeys: [],
                ChangedKeys: ["value"]);

            return Task.FromResult(new BlobVersionComparison(
                BaseVersionId: baseVersionId,
                CompareVersionId: compareVersionId,
                MetadataDiff: diff,
                ContentComparePossible: true,
                BaseSizeBytes: 480,
                CompareSizeBytes: 512,
                TextDiff: "- value: 1\n+ value: 2"));
        }

        public Task<BlobRecoveryResult> RestoreBlobVersionAsync(string containerName, string blobName, string versionId, CancellationToken ct = default)
        {
            LastRestoredVersionId = versionId;
            return Task.FromResult(new BlobRecoveryResult(BlobRecoveryState.Restored, ResultBlobPath: $"{containerName}/{blobName}"));
        }

        public Task<BlobRecoveryResult> UndeleteBlobAsync(string containerName, string blobName, CancellationToken ct = default)
        {
            UndeleteCallCount++;
            return Task.FromResult(new BlobRecoveryResult(BlobRecoveryState.Undeleted, ResultBlobPath: $"{containerName}/{blobName}"));
        }
    }

    private sealed class TestShellNavigationService : IShellNavigationService
    {
        public string? CurrentArea { get; private set; }

        public event Action? NavigationChanged;

        public void NavigateTo(string area, object? parameter = null)
        {
            CurrentArea = area;
            NavigationChanged?.Invoke();
        }
    }

    private sealed class TestNotificationService : INotificationService
    {
        private readonly List<Notification> _all = [];

        public IReadOnlyList<Notification> All => _all;

        public event Action? NotificationsChanged;

        public void ShowSuccess(string message, string? detail = null) => Add(NotificationSeverity.Success, message, detail);

        public void ShowWarning(string message, string? detail = null) => Add(NotificationSeverity.Warning, message, detail);

        public void ShowError(string message, string? detail = null, Exception? ex = null) => Add(NotificationSeverity.Error, message, detail ?? ex?.Message);

        public void ShowInfo(string message, string? detail = null) => Add(NotificationSeverity.Info, message, detail);

        public void Dismiss(Guid id)
        {
            _all.RemoveAll(candidate => candidate.Id == id);
            NotificationsChanged?.Invoke();
        }

        public void ClearAll()
        {
            _all.Clear();
            NotificationsChanged?.Invoke();
        }

        private void Add(NotificationSeverity severity, string message, string? detail)
        {
            _all.Add(new Notification(Guid.NewGuid(), severity, message, detail, DateTimeOffset.UtcNow));
            NotificationsChanged?.Invoke();
        }
    }
}