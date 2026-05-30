using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Reflection;
using SwebKit.App.Components.Storage;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

[Collection("AppDataSerial")]
public class StorageDownloadProgressTests : TestContext
{
    public StorageDownloadProgressTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);

        Services.AddSingleton<INotificationService>(new TestNotificationService());
    }

    [Fact]
    public void BlobDetailPane_ShowsProgressWhileBlobDownloadIsRunning()
    {
        var blob = CreateBlob(sizeBytes: 1024);
        var client = CreateClient(blobItems: [blob]);

        var cut = RenderComponent<BlobDetailPane>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.ContainerName, "reports")
            .Add(p => p.Blob, blob));

        cut.WaitForAssertion(() => Assert.Contains("Download", cut.Markup));

        BeginPrivateAsyncMethod(cut, "DownloadAsync");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Downloading large.csv", cut.Markup);
            Assert.Contains(FormatExpectedProgress(512, 1024), cut.Markup);
        });

        client.CompletePendingDownload(1024);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Downloaded to:", cut.Markup);
            Assert.DoesNotContain("Downloading large.csv", cut.Markup);
        });
    }

    [Fact]
    public void BlobDetailPane_VersionDownload_PassesVersionIdAndShowsProgress()
    {
        var version = new BlobVersionItem("2026-03-20T10:00:00Z", DateTimeOffset.UtcNow, 2048, false);
        var blob = CreateBlob(sizeBytes: 2048);
        var client = CreateClient(blobItems: [blob], versions: [version]);

        var cut = RenderComponent<BlobDetailPane>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.ContainerName, "reports")
            .Add(p => p.Blob, blob));

        cut.WaitForAssertion(() => Assert.Contains("Versions", cut.Markup));
        cut.FindAll("button")
            .First(button => string.Equals(button.TextContent.Trim(), "Versions", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => Assert.Contains(version.VersionId, cut.Markup));

        BeginPrivateAsyncMethod(cut, "DownloadVersionAsync", version);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(version.VersionId, client.LastVersionId);
            Assert.Contains("Downloading version", cut.Markup);
            Assert.Contains(FormatExpectedProgress(512, 2048), cut.Markup);
        });

        client.CompletePendingDownload(2048);

        cut.WaitForAssertion(() => Assert.Contains("Downloaded version to:", cut.Markup));
    }

    [Fact]
    public void StorageBlobList_ShowsProgressForContextMenuDownload()
    {
        var blob = CreateBlob(sizeBytes: 2048);
        var client = CreateClient(blobItems: [blob]);

        var cut = RenderComponent<StorageBlobList>(ps => ps
            .Add(p => p.Client, client)
            .Add(p => p.ContainerName, "reports"));

        cut.WaitForAssertion(() => Assert.Contains("large.csv", cut.Markup));

        cut.Find("tbody tr").ContextMenu(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ClientX = 12, ClientY = 18 });
        cut.WaitForAssertion(() => Assert.Contains("Download", cut.Markup));

        BeginPrivateAsyncMethod(cut, "DownloadAsync", blob);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Downloading large.csv", cut.Markup);
            Assert.Contains(FormatExpectedProgress(512, 2048), cut.Markup);
        });

        client.CompletePendingDownload(2048);

        cut.WaitForAssertion(() => Assert.Contains("Downloaded to:", cut.Markup));
    }

    private static PendingDownloadStorageClient CreateClient(
        IReadOnlyList<StorageBlobItem>? blobItems = null,
        IReadOnlyList<BlobVersionItem>? versions = null)
    {
        var primaryBlob = blobItems?.FirstOrDefault() ?? CreateBlob();
        return new PendingDownloadStorageClient(
            blobItems ?? [primaryBlob],
            new BlobProperties(
                Name: primaryBlob.Name,
                SizeBytes: primaryBlob.SizeBytes ?? 1024,
                ContentType: primaryBlob.ContentType ?? "text/csv",
                LastModified: primaryBlob.LastModified ?? DateTimeOffset.UtcNow,
                ETag: primaryBlob.ETag ?? "\"etag\"",
                LeaseStatus: null,
                LeaseState: null,
                AccessTier: null,
                AccessTierInferred: null,
                ContentEncoding: null,
                ContentLanguage: null,
                CacheControl: null,
                Metadata: new Dictionary<string, string>(),
                Tags: new Dictionary<string, string>()),
            new StorageBlobContent(
                ContainerName: "reports",
                BlobName: primaryBlob.Name,
                Content: "a,b,c",
                ContentType: primaryBlob.ContentType,
                TotalSizeBytes: primaryBlob.SizeBytes ?? 1024,
                WasTruncated: false,
                IsBinary: false),
            versions ?? []);
    }

    private static StorageBlobItem CreateBlob(long sizeBytes = 1024) =>
        new("reports/large.csv", false, sizeBytes, "text/csv", DateTimeOffset.UtcNow, "\"etag\"");

    private static void BeginPrivateAsyncMethod<TComponent>(IRenderedComponent<TComponent> cut, string methodName, params object?[] arguments)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        var method = typeof(TComponent).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        cut.InvokeAsync(() =>
        {
            _ = method!.Invoke(cut.Instance, arguments);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
    }

    private static string FormatExpectedProgress(long transferredBytes, long totalBytes)
        => $"{FormatExpectedSize(transferredBytes)} / {FormatExpectedSize(totalBytes)}";

    private static string FormatExpectedSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        return string.Format(CultureInfo.CurrentCulture, "{0:F1} KB", bytes / 1024d);
    }

    private sealed class PendingDownloadStorageClient(
        IReadOnlyList<StorageBlobItem> blobItems,
        BlobProperties properties,
        StorageBlobContent content,
        IReadOnlyList<BlobVersionItem> versions) : IStorageClient
    {
        private TaskCompletionSource? _pendingDownload;
        private IProgress<long>? _progress;

        public StorageConfig Config { get; } = new()
        {
            Id = "test-storage",
            DisplayName = "Test Storage",
            AccountName = "testaccount",
            UseAad = true
        };

        public string? LastVersionId { get; private set; }

        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<IReadOnlyList<StorageContainerItem>> ListContainersAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StorageContainerItem>>([new StorageContainerItem("reports", null, null, null)]);

        public Task<StorageBlobPage> ListBlobsAsync(string containerName, string prefix, string? continuationToken = null, int pageSize = 100,
            CancellationToken ct = default) =>
            Task.FromResult(new StorageBlobPage(blobItems, null));

        public Task<BlobProperties> GetBlobPropertiesAsync(string containerName, string blobName, CancellationToken ct = default) =>
            Task.FromResult(properties);

        public Task<StorageBlobContent> GetBlobContentAsync(string containerName, string blobName, int maxBytes = 524_288,
            CancellationToken ct = default) =>
            Task.FromResult(content);

        public Task<string> GetBlobSasUrlAsync(string containerName, string blobName, TimeSpan expiry, CancellationToken ct = default) =>
            Task.FromResult("https://example.invalid/blob?sas=1");

        public Task DownloadBlobAsync(string containerName, string blobName, Stream destination, IProgress<long>? progress = null,
            string? versionId = null, CancellationToken ct = default)
        {
            LastVersionId = versionId;
            _progress = progress;
            _pendingDownload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _progress?.Report(512);
            return _pendingDownload.Task;
        }

        public void CompletePendingDownload(long finalBytes)
        {
            _progress?.Report(finalBytes);
            _pendingDownload?.TrySetResult();
        }

        public Task<IReadOnlyList<BlobVersionItem>> ListBlobVersionsAsync(string containerName, string blobName, CancellationToken ct = default) =>
            Task.FromResult(versions);

        public Task<string> GetContainerSasUrlAsync(string containerName, TimeSpan expiry, CancellationToken ct = default) =>
            Task.FromResult("https://example.invalid/container?sas=1");

        public Task<StorageCapabilities> GetStorageCapabilitiesAsync(CancellationToken ct = default) =>
            Task.FromResult(new StorageCapabilities(false, false, false, false, false, false));

        public Task<BlobMutationResult> UploadBlobAsync(BlobUploadOptions options, Stream source, IProgress<long>? progress = null, CancellationToken ct = default) =>
            Task.FromResult(new BlobMutationResult(false));

        public Task<BlobMutationResult> CopyBlobAsync(BlobCopyOptions options, CancellationToken ct = default) =>
            Task.FromResult(new BlobMutationResult(false));

        public Task<BlobMutationResult> SetBlobMetadataAsync(string containerName, string blobName, IDictionary<string, string> metadata, string? ifMatchEtag = null, CancellationToken ct = default) =>
            Task.FromResult(new BlobMutationResult(false));

        public Task<BlobVersionComparison> GetVersionComparisonAsync(string containerName, string blobName, string baseVersionId, string? compareVersionId = null, CancellationToken ct = default) =>
            throw new NotSupportedException("Not implemented in test stub.");

        public Task<BlobRecoveryResult> RestoreBlobVersionAsync(string containerName, string blobName, string versionId, CancellationToken ct = default) =>
            Task.FromResult(new BlobRecoveryResult(BlobRecoveryState.Unsupported));

        public Task<BlobRecoveryResult> UndeleteBlobAsync(string containerName, string blobName, CancellationToken ct = default) =>
            Task.FromResult(new BlobRecoveryResult(BlobRecoveryState.Unsupported));
    }

    private sealed class TestNotificationService : INotificationService
    {
        public IReadOnlyList<Notification> All => [];
        public event Action? NotificationsChanged;

        public void ShowSuccess(string message, string? detail = null) => NotificationsChanged?.Invoke();
        public void ShowWarning(string message, string? detail = null) => NotificationsChanged?.Invoke();
        public void ShowError(string message, string? detail = null, Exception? ex = null) => NotificationsChanged?.Invoke();
        public void ShowInfo(string message, string? detail = null) => NotificationsChanged?.Invoke();
        public void Dismiss(Guid id) { }
        public void ClearAll() { }
    }
}