using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

public interface IStorageClient
{
    StorageConfig Config { get; }

    /// <summary>
    /// Verifies connectivity. Returns true on success; throws descriptive exception on failure.
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>Returns all containers visible to the configured credential.</summary>
    Task<IReadOnlyList<StorageContainerItem>> ListContainersAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns one page of blobs and virtual-folder prefixes inside a container.
    /// prefix = "" for root. Uses "/" delimiter for hierarchical listing.
    /// </summary>
    Task<StorageBlobPage> ListBlobsAsync(
        string containerName,
        string prefix,
        string? continuationToken = null,
        int pageSize = 100,
        CancellationToken ct = default);

    /// <summary>Returns full properties for a single blob (metadata, tags, tiers, lease, etc.).</summary>
    Task<BlobProperties> GetBlobPropertiesAsync(
        string containerName,
        string blobName,
        CancellationToken ct = default);

    /// <summary>
    /// Reads blob content as UTF-8 text up to maxBytes.
    /// Returns WasTruncated = true if blob is larger than maxBytes.
    /// Returns IsBinary = true if content-type is not text/*, application/json, application/xml, or application/x-www-form-urlencoded.
    /// </summary>
    Task<StorageBlobContent> GetBlobContentAsync(
        string containerName,
        string blobName,
        int maxBytes = 524_288,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a service SAS URI for a blob. Requires shared key access.
    /// Throws RequestFailedException (AuthorizationPermissionMismatch) if shared key is disallowed.
    /// </summary>
    Task<string> GetBlobSasUrlAsync(
        string containerName,
        string blobName,
        TimeSpan expiry,
        CancellationToken ct = default);

    /// <summary>Streams blob content to destination stream. No size cap.</summary>
    Task DownloadBlobAsync(
        string containerName,
        string blobName,
        Stream destination,
        IProgress<long>? progress = null,
        string? versionId = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<BlobVersionItem>> ListBlobVersionsAsync(
        string containerName, string blobName, CancellationToken ct = default);

    Task<string> GetContainerSasUrlAsync(
        string containerName, TimeSpan expiry, CancellationToken ct = default);

    // --- Mutation methods ---

    /// <summary>Returns a best-effort snapshot of versioning, soft-delete, and mutation capabilities for the account.</summary>
    Task<StorageCapabilities> GetStorageCapabilitiesAsync(CancellationToken ct = default);

    /// <summary>Uploads a stream to a blob. Reports progress via IProgress if provided. Returns a mutation result — never throws.</summary>
    Task<BlobMutationResult> UploadBlobAsync(BlobUploadOptions options, Stream source, IProgress<long>? progress = null, CancellationToken ct = default);

    /// <summary>Copies a blob within the same account. Supports versioned source and overwrite semantics. Returns a mutation result — never throws.</summary>
    Task<BlobMutationResult> CopyBlobAsync(BlobCopyOptions options, CancellationToken ct = default);

    /// <summary>Sets metadata on a blob. Uses ETag conditional write when ifMatchEtag is non-null. Returns a mutation result — never throws.</summary>
    Task<BlobMutationResult> SetBlobMetadataAsync(string containerName, string blobName, IDictionary<string, string> metadata, string? ifMatchEtag = null, CancellationToken ct = default);

    /// <summary>Fetches properties for two versions and computes a metadata diff. Attempts a text content diff when both versions are under 100 KB and text-based.</summary>
    Task<BlobVersionComparison> GetVersionComparisonAsync(string containerName, string blobName, string baseVersionId, string? compareVersionId = null, CancellationToken ct = default);

    /// <summary>Restores a versioned blob by copying it forward to the current path. Returns a recovery result — never throws.</summary>
    Task<BlobRecoveryResult> RestoreBlobVersionAsync(string containerName, string blobName, string versionId, CancellationToken ct = default);

    /// <summary>Undeletes a soft-deleted blob. Returns Unsupported when soft delete is not enabled or the blob is not found. Never throws.</summary>
    Task<BlobRecoveryResult> UndeleteBlobAsync(string containerName, string blobName, CancellationToken ct = default);

    /// <summary>Lists soft-deleted blobs in a container that are still within retention.</summary>
    Task<IReadOnlyList<DeletedBlobItem>> ListDeletedBlobsAsync(string containerName, string? prefix = null, CancellationToken ct = default);
}

public interface IStorageClientFactory
{
    IStorageClient Create(StorageConfig config);
}
