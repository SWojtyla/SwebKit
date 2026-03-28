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
        CancellationToken ct = default);

    Task<IReadOnlyList<BlobVersionItem>> ListBlobVersionsAsync(
        string containerName, string blobName, CancellationToken ct = default);

    Task<string> GetContainerSasUrlAsync(
        string containerName, TimeSpan expiry, CancellationToken ct = default);
}
