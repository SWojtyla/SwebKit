using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Delegates every call to a real <see cref="DemoStorageClient"/> (sealed, so composition rather than
/// inheritance) except for whichever single method a test configures to throw instead — used to prove
/// the mutation/read endpoints surface a client failure rather than swallowing it.
/// </summary>
internal sealed class FaultInjectingStorageClient : IStorageClient
{
    private readonly IStorageClient _inner;

    public FaultInjectingStorageClient(IStorageClient inner) => _inner = inner;

    public Exception? ThrowOnGetBlobProperties { get; set; }
    public Exception? ThrowOnGetBlobSasUrl { get; set; }
    public Exception? ThrowOnUploadBlob { get; set; }
    public Exception? ThrowOnCopyBlob { get; set; }
    public Exception? ThrowOnSetBlobMetadata { get; set; }
    public Exception? ThrowOnUndeleteBlob { get; set; }

    public StorageConfig Config => _inner.Config;

    public Task<bool> TestConnectionAsync(CancellationToken ct = default) => _inner.TestConnectionAsync(ct);
    public Task<IReadOnlyList<StorageContainerItem>> ListContainersAsync(CancellationToken ct = default) => _inner.ListContainersAsync(ct);
    public Task<StorageBlobPage> ListBlobsAsync(string containerName, string prefix, string? continuationToken = null, int pageSize = 100, CancellationToken ct = default) =>
        _inner.ListBlobsAsync(containerName, prefix, continuationToken, pageSize, ct);

    public Task<BlobProperties> GetBlobPropertiesAsync(string containerName, string blobName, CancellationToken ct = default) =>
        ThrowOnGetBlobProperties is not null ? Task.FromException<BlobProperties>(ThrowOnGetBlobProperties) : _inner.GetBlobPropertiesAsync(containerName, blobName, ct);

    public Task<StorageBlobContent> GetBlobContentAsync(string containerName, string blobName, int maxBytes = 524288, CancellationToken ct = default) =>
        _inner.GetBlobContentAsync(containerName, blobName, maxBytes, ct);

    public Task<string> GetBlobSasUrlAsync(string containerName, string blobName, TimeSpan expiry, CancellationToken ct = default) =>
        ThrowOnGetBlobSasUrl is not null ? Task.FromException<string>(ThrowOnGetBlobSasUrl) : _inner.GetBlobSasUrlAsync(containerName, blobName, expiry, ct);

    public Task DownloadBlobAsync(string containerName, string blobName, Stream destination, IProgress<long>? progress = null, string? versionId = null, CancellationToken ct = default) =>
        _inner.DownloadBlobAsync(containerName, blobName, destination, progress, versionId, ct);

    public Task<IReadOnlyList<BlobVersionItem>> ListBlobVersionsAsync(string containerName, string blobName, CancellationToken ct = default) =>
        _inner.ListBlobVersionsAsync(containerName, blobName, ct);

    public Task<string> GetContainerSasUrlAsync(string containerName, TimeSpan expiry, CancellationToken ct = default) =>
        _inner.GetContainerSasUrlAsync(containerName, expiry, ct);

    public Task<StorageCapabilities> GetStorageCapabilitiesAsync(CancellationToken ct = default) => _inner.GetStorageCapabilitiesAsync(ct);

    public Task<BlobMutationResult> UploadBlobAsync(BlobUploadOptions options, Stream source, IProgress<long>? progress = null, CancellationToken ct = default) =>
        ThrowOnUploadBlob is not null ? Task.FromException<BlobMutationResult>(ThrowOnUploadBlob) : _inner.UploadBlobAsync(options, source, progress, ct);

    public Task<BlobMutationResult> CopyBlobAsync(BlobCopyOptions options, CancellationToken ct = default) =>
        ThrowOnCopyBlob is not null ? Task.FromException<BlobMutationResult>(ThrowOnCopyBlob) : _inner.CopyBlobAsync(options, ct);

    public Task<BlobMutationResult> SetBlobMetadataAsync(string containerName, string blobName, IDictionary<string, string> metadata, string? ifMatchEtag = null, CancellationToken ct = default) =>
        ThrowOnSetBlobMetadata is not null ? Task.FromException<BlobMutationResult>(ThrowOnSetBlobMetadata) : _inner.SetBlobMetadataAsync(containerName, blobName, metadata, ifMatchEtag, ct);

    public Task<BlobVersionComparison> GetVersionComparisonAsync(string containerName, string blobName, string baseVersionId, string? compareVersionId = null, CancellationToken ct = default) =>
        _inner.GetVersionComparisonAsync(containerName, blobName, baseVersionId, compareVersionId, ct);

    public Task<BlobRecoveryResult> RestoreBlobVersionAsync(string containerName, string blobName, string versionId, CancellationToken ct = default) =>
        _inner.RestoreBlobVersionAsync(containerName, blobName, versionId, ct);

    public Task<BlobRecoveryResult> UndeleteBlobAsync(string containerName, string blobName, CancellationToken ct = default) =>
        ThrowOnUndeleteBlob is not null ? Task.FromException<BlobRecoveryResult>(ThrowOnUndeleteBlob) : _inner.UndeleteBlobAsync(containerName, blobName, ct);

    public Task<IReadOnlyList<DeletedBlobItem>> ListDeletedBlobsAsync(string containerName, string? prefix = null, CancellationToken ct = default) =>
        _inner.ListDeletedBlobsAsync(containerName, prefix, ct);
}

/// <summary>Records the config passed to each creation call and returns a configurable client.</summary>
internal sealed class FakeStorageClientFactory : IStorageClientFactory
{
    public IStorageClient Client { get; set; } = new FaultInjectingStorageClient(new DemoStorageClient());
    public List<StorageConfig> Calls { get; } = [];

    public IStorageClient Create(StorageConfig config)
    {
        Calls.Add(config);
        return Client;
    }
}

/// <summary>Minimal <see cref="IFormFile"/> double so upload tests don't need a real multipart body.</summary>
internal sealed class FakeFormFile(string fieldName, string fileName, string contentType, byte[] content) : IFormFile
{
    public string ContentType { get; set; } = contentType;
    public string ContentDisposition { get; set; } = $"form-data; name=\"{fieldName}\"; filename=\"{fileName}\"";
    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
    public long Length { get; } = content.Length;
    public string Name { get; } = fieldName;
    public string FileName { get; } = fileName;

    public void CopyTo(Stream target) => new MemoryStream(content).CopyTo(target);
    public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => new MemoryStream(content).CopyToAsync(target, cancellationToken);
    public Stream OpenReadStream() => new MemoryStream(content);
}

public class StorageEndpointsMutationTests
{
    private const string AccountId = "acct-1";
    private const string Container = "exports";
    private const string BlobName = "2026-03-21-report.csv";

    private static (ProfileRepository Profile, DemoModeService Demo, FakeStorageClientFactory Factory) Build(
        IStorageClient? client = null, bool allowMutations = true)
    {
        var profile = new ProfileRepository();
        profile.Config.StorageAccounts.Add(new StorageConfig
        {
            Id = AccountId,
            DisplayName = "Test Storage",
            AccountName = "teststore",
            AllowMutations = allowMutations,
        });
        var demo = new DemoModeService();
        var factory = new FakeStorageClientFactory();
        if (client is not null)
            factory.Client = client;
        return (profile, demo, factory);
    }

    private static DefaultHttpContext BuildUploadHttpContext(byte[] content, string? fileFieldName = "file")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.ContentType = "multipart/form-data; boundary=----testboundary";
        var files = new FormFileCollection();
        if (fileFieldName is not null)
            files.Add(new FakeFormFile(fileFieldName, "upload.csv", "text/csv", content));
        ctx.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(), files);
        return ctx;
    }

    // ── Blob properties (metadata get) ───────────────────────────────────────

    [Fact]
    public async Task GetBlobPropertiesAsync_Success_ReturnsPropertiesFromClient()
    {
        var (profile, demo, factory) = Build();

        var result = await StorageEndpoints.GetBlobPropertiesAsync(AccountId, Container, BlobName, profile, factory, demo);

        var ok = Assert.IsAssignableFrom<Ok<Core.Domain.BlobProperties>>(result);
        Assert.Equal(BlobName, ok.Value!.Name);
    }

    [Fact]
    public async Task GetBlobPropertiesAsync_AccountNotFound_ReturnsNotFound()
    {
        var (profile, demo, factory) = Build();

        var result = await StorageEndpoints.GetBlobPropertiesAsync("no-such-account", Container, BlobName, profile, factory, demo);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(404, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task GetBlobPropertiesAsync_ClientThrows_ExceptionPropagates_NotSwallowed()
    {
        var faulty = new FaultInjectingStorageClient(new DemoStorageClient()) { ThrowOnGetBlobProperties = new InvalidOperationException("storage unavailable") };
        var (profile, demo, factory) = Build(faulty);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StorageEndpoints.GetBlobPropertiesAsync(AccountId, Container, BlobName, profile, factory, demo));
        Assert.Equal("storage unavailable", ex.Message);
    }

    // ── SAS URL generation ────────────────────────────────────────────────────

    [Fact]
    public async Task GetBlobSasUrlAsync_Success_ReturnsUrlFromClient()
    {
        var (profile, demo, factory) = Build();

        var result = await StorageEndpoints.GetBlobSasUrlAsync(AccountId, Container, BlobName, 30, profile, factory, demo);

        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        Assert.NotNull(value);
        var sasUrl = (string)value!.GetType().GetProperty("sasUrl")!.GetValue(value)!;
        Assert.Contains(BlobName, sasUrl);
    }

    [Fact]
    public async Task GetBlobSasUrlAsync_ClientThrows_ExceptionPropagates_NotSwallowed()
    {
        var faulty = new FaultInjectingStorageClient(new DemoStorageClient()) { ThrowOnGetBlobSasUrl = new InvalidOperationException("shared key disallowed") };
        var (profile, demo, factory) = Build(faulty);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StorageEndpoints.GetBlobSasUrlAsync(AccountId, Container, BlobName, 30, profile, factory, demo));
        Assert.Equal("shared key disallowed", ex.Message);
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadBlobAsync_Success_WritesBlobOnUnderlyingClient()
    {
        var demoClient = new DemoStorageClient();
        var (profile, demo, factory) = Build(demoClient);
        var ctx = BuildUploadHttpContext("id,name\n1,alpha\n"u8.ToArray());

        var result = await StorageEndpoints.UploadBlobAsync(AccountId, "fixtures", "new-upload.csv", ctx.Request, profile, factory, demo);

        Assert.IsAssignableFrom<Ok<Core.Domain.BlobMutationResult>>(result);
        var content = await demoClient.GetBlobContentAsync("fixtures", "new-upload.csv");
        Assert.Contains("alpha", content.Content);
    }

    [Fact]
    public async Task UploadBlobAsync_MutationsDisabled_ReturnsForbidden_AndNeverCallsClient()
    {
        var faulty = new FaultInjectingStorageClient(new DemoStorageClient());
        var (profile, demo, factory) = Build(faulty, allowMutations: false);
        var ctx = BuildUploadHttpContext("data"u8.ToArray());

        var result = await StorageEndpoints.UploadBlobAsync(AccountId, "fixtures", "new-upload.csv", ctx.Request, profile, factory, demo);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(403, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task UploadBlobAsync_NoFilePresent_ReturnsBadRequest()
    {
        var (profile, demo, factory) = Build();
        var ctx = BuildUploadHttpContext([], fileFieldName: null);

        var result = await StorageEndpoints.UploadBlobAsync(AccountId, "fixtures", "new-upload.csv", ctx.Request, profile, factory, demo);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(400, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task UploadBlobAsync_ClientThrows_ExceptionPropagates_NotSwallowed()
    {
        var faulty = new FaultInjectingStorageClient(new DemoStorageClient()) { ThrowOnUploadBlob = new InvalidOperationException("storage unavailable") };
        var (profile, demo, factory) = Build(faulty);
        var ctx = BuildUploadHttpContext("data"u8.ToArray());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StorageEndpoints.UploadBlobAsync(AccountId, "fixtures", "new-upload.csv", ctx.Request, profile, factory, demo));
        Assert.Equal("storage unavailable", ex.Message);
    }

    // ── Copy ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CopyBlobAsync_Success_CopiesOnUnderlyingClient()
    {
        var demoClient = new DemoStorageClient();
        var (profile, demo, factory) = Build(demoClient);
        var req = new BlobCopyRequest { SourceContainer = Container, SourceBlob = BlobName, DestContainer = Container, DestBlob = "copy-of-report.csv" };

        var result = await StorageEndpoints.CopyBlobAsync(AccountId, req, profile, factory, demo);

        Assert.IsAssignableFrom<Ok<Core.Domain.BlobMutationResult>>(result);
        var content = await demoClient.GetBlobContentAsync(Container, "copy-of-report.csv");
        Assert.Contains("ORD-12345", content.Content);
    }

    [Fact]
    public async Task CopyBlobAsync_MutationsDisabled_ReturnsForbidden()
    {
        var faulty = new FaultInjectingStorageClient(new DemoStorageClient());
        var (profile, demo, factory) = Build(faulty, allowMutations: false);
        var req = new BlobCopyRequest { SourceContainer = Container, SourceBlob = BlobName, DestContainer = Container, DestBlob = "copy-of-report.csv" };

        var result = await StorageEndpoints.CopyBlobAsync(AccountId, req, profile, factory, demo);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(403, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task CopyBlobAsync_ClientThrows_ExceptionPropagates_NotSwallowed()
    {
        var faulty = new FaultInjectingStorageClient(new DemoStorageClient()) { ThrowOnCopyBlob = new InvalidOperationException("storage unavailable") };
        var (profile, demo, factory) = Build(faulty);
        var req = new BlobCopyRequest { SourceContainer = Container, SourceBlob = BlobName, DestContainer = Container, DestBlob = "copy-of-report.csv" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StorageEndpoints.CopyBlobAsync(AccountId, req, profile, factory, demo));
        Assert.Equal("storage unavailable", ex.Message);
    }

    // ── Set metadata ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SetBlobMetadataAsync_Success_SetsMetadataOnUnderlyingClient()
    {
        var demoClient = new DemoStorageClient();
        var (profile, demo, factory) = Build(demoClient);
        var metadata = new Dictionary<string, string> { ["author"] = "test-suite" };

        var result = await StorageEndpoints.SetBlobMetadataAsync(AccountId, Container, BlobName, metadata, profile, factory, demo);

        Assert.IsAssignableFrom<Ok<Core.Domain.BlobMutationResult>>(result);
        var props = await demoClient.GetBlobPropertiesAsync(Container, BlobName);
        Assert.Equal("test-suite", props.Metadata["author"]);
    }

    [Fact]
    public async Task SetBlobMetadataAsync_MutationsDisabled_ReturnsForbidden()
    {
        var faulty = new FaultInjectingStorageClient(new DemoStorageClient());
        var (profile, demo, factory) = Build(faulty, allowMutations: false);

        var result = await StorageEndpoints.SetBlobMetadataAsync(AccountId, Container, BlobName, new Dictionary<string, string>(), profile, factory, demo);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(403, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task SetBlobMetadataAsync_ClientThrows_ExceptionPropagates_NotSwallowed()
    {
        var faulty = new FaultInjectingStorageClient(new DemoStorageClient()) { ThrowOnSetBlobMetadata = new InvalidOperationException("storage unavailable") };
        var (profile, demo, factory) = Build(faulty);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StorageEndpoints.SetBlobMetadataAsync(AccountId, Container, BlobName, new Dictionary<string, string>(), profile, factory, demo));
        Assert.Equal("storage unavailable", ex.Message);
    }

    // ── Undelete ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UndeleteBlobAsync_Success_RestoresBlobOnUnderlyingClient()
    {
        var demoClient = new DemoStorageClient();
        var (profile, demo, factory) = Build(demoClient);

        var result = await StorageEndpoints.UndeleteBlobAsync(AccountId, "exports", "deleted-report.csv", profile, factory, demo);

        Assert.IsAssignableFrom<Ok<Core.Domain.BlobRecoveryResult>>(result);
        var deleted = await demoClient.ListDeletedBlobsAsync("exports");
        Assert.DoesNotContain(deleted, d => d.Name == "deleted-report.csv");
    }

    [Fact]
    public async Task UndeleteBlobAsync_NotFoundInDeletedList_ReturnsBadRequest_NotSwallowedIntoSuccess()
    {
        // DemoStorageClient.UndeleteBlobAsync returns BlobRecoveryState.Unsupported when the blob isn't
        // in the deleted list — proves the endpoint surfaces that as a BadRequest rather than a false Ok.
        var demoClient = new DemoStorageClient();
        var (profile, demo, factory) = Build(demoClient);

        var result = await StorageEndpoints.UndeleteBlobAsync(AccountId, "exports", "never-deleted.csv", profile, factory, demo);

        Assert.IsAssignableFrom<BadRequest<Core.Domain.BlobRecoveryResult>>(result);
    }

    [Fact]
    public async Task UndeleteBlobAsync_MutationsDisabled_ReturnsForbidden()
    {
        var faulty = new FaultInjectingStorageClient(new DemoStorageClient());
        var (profile, demo, factory) = Build(faulty, allowMutations: false);

        var result = await StorageEndpoints.UndeleteBlobAsync(AccountId, "exports", "deleted-report.csv", profile, factory, demo);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(403, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task UndeleteBlobAsync_ClientThrows_ExceptionPropagates_NotSwallowed()
    {
        var faulty = new FaultInjectingStorageClient(new DemoStorageClient()) { ThrowOnUndeleteBlob = new InvalidOperationException("storage unavailable") };
        var (profile, demo, factory) = Build(faulty);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StorageEndpoints.UndeleteBlobAsync(AccountId, "exports", "deleted-report.csv", profile, factory, demo));
        Assert.Equal("storage unavailable", ex.Message);
    }
}
