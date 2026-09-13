using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Services;

namespace SwebKit.Sidecar.Tests;

/// <summary>Records how many distinct clients were built, and tracks async disposal.</summary>
internal sealed class TrackingStorageClient : IStorageClient, IAsyncDisposable
{
    private readonly IStorageClient _inner = new DemoStorageClient();

    public int DisposeAsyncCallCount { get; private set; }
    public bool WasDisposedAsync => DisposeAsyncCallCount > 0;

    public ValueTask DisposeAsync()
    {
        DisposeAsyncCallCount++;
        return ValueTask.CompletedTask;
    }

    public StorageConfig Config => _inner.Config;
    public Task<bool> TestConnectionAsync(CancellationToken ct = default) => _inner.TestConnectionAsync(ct);
    public Task<IReadOnlyList<StorageContainerItem>> ListContainersAsync(CancellationToken ct = default) => _inner.ListContainersAsync(ct);
    public Task<StorageBlobPage> ListBlobsAsync(string containerName, string prefix, string? continuationToken = null, int pageSize = 100, CancellationToken ct = default) =>
        _inner.ListBlobsAsync(containerName, prefix, continuationToken, pageSize, ct);
    public Task<BlobProperties> GetBlobPropertiesAsync(string containerName, string blobName, CancellationToken ct = default) =>
        _inner.GetBlobPropertiesAsync(containerName, blobName, ct);
    public Task<StorageBlobContent> GetBlobContentAsync(string containerName, string blobName, int maxBytes = 524288, CancellationToken ct = default) =>
        _inner.GetBlobContentAsync(containerName, blobName, maxBytes, ct);
    public Task<string> GetBlobSasUrlAsync(string containerName, string blobName, TimeSpan expiry, CancellationToken ct = default) =>
        _inner.GetBlobSasUrlAsync(containerName, blobName, expiry, ct);
    public Task DownloadBlobAsync(string containerName, string blobName, Stream destination, IProgress<long>? progress = null, string? versionId = null, CancellationToken ct = default) =>
        _inner.DownloadBlobAsync(containerName, blobName, destination, progress, versionId, ct);
    public Task<IReadOnlyList<BlobVersionItem>> ListBlobVersionsAsync(string containerName, string blobName, CancellationToken ct = default) =>
        _inner.ListBlobVersionsAsync(containerName, blobName, ct);
    public Task<string> GetContainerSasUrlAsync(string containerName, TimeSpan expiry, CancellationToken ct = default) =>
        _inner.GetContainerSasUrlAsync(containerName, expiry, ct);
    public Task<StorageCapabilities> GetStorageCapabilitiesAsync(CancellationToken ct = default) => _inner.GetStorageCapabilitiesAsync(ct);
    public Task<BlobMutationResult> UploadBlobAsync(BlobUploadOptions options, Stream source, IProgress<long>? progress = null, CancellationToken ct = default) =>
        _inner.UploadBlobAsync(options, source, progress, ct);
    public Task<BlobMutationResult> CopyBlobAsync(BlobCopyOptions options, CancellationToken ct = default) => _inner.CopyBlobAsync(options, ct);
    public Task<BlobMutationResult> SetBlobMetadataAsync(string containerName, string blobName, IDictionary<string, string> metadata, string? ifMatchEtag = null, CancellationToken ct = default) =>
        _inner.SetBlobMetadataAsync(containerName, blobName, metadata, ifMatchEtag, ct);
    public Task<BlobVersionComparison> GetVersionComparisonAsync(string containerName, string blobName, string baseVersionId, string? compareVersionId = null, CancellationToken ct = default) =>
        _inner.GetVersionComparisonAsync(containerName, blobName, baseVersionId, compareVersionId, ct);
    public Task<BlobRecoveryResult> RestoreBlobVersionAsync(string containerName, string blobName, string versionId, CancellationToken ct = default) =>
        _inner.RestoreBlobVersionAsync(containerName, blobName, versionId, ct);
    public Task<BlobRecoveryResult> UndeleteBlobAsync(string containerName, string blobName, CancellationToken ct = default) =>
        _inner.UndeleteBlobAsync(containerName, blobName, ct);
    public Task<IReadOnlyList<DeletedBlobItem>> ListDeletedBlobsAsync(string containerName, string? prefix = null, CancellationToken ct = default) =>
        _inner.ListDeletedBlobsAsync(containerName, prefix, ct);
}

internal sealed class TrackingStorageClientFactory : IStorageClientFactory
{
    public List<StorageConfig> Calls { get; } = [];
    public List<TrackingStorageClient> CreatedClients { get; } = [];

    public IStorageClient Create(StorageConfig config)
    {
        Calls.Add(config);
        var client = new TrackingStorageClient();
        CreatedClients.Add(client);
        return client;
    }
}

/// <summary>
/// Covers the account-level caching this pool adds in front of <see cref="IStorageClientFactory"/> —
/// every real (non-demo) storage endpoint request used to build a fresh <c>BlobServiceClient</c> (and,
/// for AAD accounts, a fresh credential/token chain) on every call; this pool makes that once-per-account.
/// </summary>
public class SidecarStorageConnectionPoolTests
{
    private static StorageConfig Config(string id) => new() { Id = id, DisplayName = id, AccountName = id };

    [Fact]
    public void GetOrCreate_CachesByAccountId_DoesNotRecreateOnRepeatedCalls()
    {
        var factory = new TrackingStorageClientFactory();
        var pool = new SidecarStorageConnectionPool(factory);
        var config = Config("acct-1");

        var first = pool.GetOrCreate(config);
        var second = pool.GetOrCreate(config);

        Assert.Same(first, second);
        Assert.Single(factory.Calls);
    }

    [Fact]
    public void GetOrCreate_DifferentAccounts_CreatesSeparateClients()
    {
        var factory = new TrackingStorageClientFactory();
        var pool = new SidecarStorageConnectionPool(factory);

        var a = pool.GetOrCreate(Config("acct-a"));
        var b = pool.GetOrCreate(Config("acct-b"));

        Assert.NotSame(a, b);
        Assert.Equal(2, factory.Calls.Count);
    }

    [Fact]
    public void Evict_DisposesClient_AndForcesRebuildOnNextCall()
    {
        var factory = new TrackingStorageClientFactory();
        var pool = new SidecarStorageConnectionPool(factory);
        var config = Config("acct-1");

        var first = (TrackingStorageClient)pool.GetOrCreate(config);
        pool.Evict("acct-1");

        Assert.True(first.WasDisposedAsync);
        var second = pool.GetOrCreate(config);
        Assert.NotSame(first, second);
        Assert.Equal(2, factory.Calls.Count);
    }

    [Fact]
    public void InvalidateAll_DisposesEveryCachedClient_ButPoolStaysUsable()
    {
        var factory = new TrackingStorageClientFactory();
        var pool = new SidecarStorageConnectionPool(factory);
        var a = (TrackingStorageClient)pool.GetOrCreate(Config("acct-a"));
        var b = (TrackingStorageClient)pool.GetOrCreate(Config("acct-b"));

        // Simulates a profile save: any of these accounts' connection string/auth mode may have
        // changed, so every cached client must be dropped rather than reused with stale credentials.
        pool.InvalidateAll();

        Assert.True(a.WasDisposedAsync);
        Assert.True(b.WasDisposedAsync);

        var rebuilt = pool.GetOrCreate(Config("acct-a"));
        Assert.NotSame(a, rebuilt);
    }

    [Fact]
    public async Task DisposeAsync_DisposesAllCachedClients()
    {
        var factory = new TrackingStorageClientFactory();
        var pool = new SidecarStorageConnectionPool(factory);
        var a = (TrackingStorageClient)pool.GetOrCreate(Config("acct-a"));

        await pool.DisposeAsync();

        Assert.True(a.WasDisposedAsync);
    }
}
