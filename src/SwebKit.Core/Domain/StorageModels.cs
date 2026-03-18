namespace SwebKit.Core.Domain;

public sealed record StorageContainerItem(
    string Name,
    DateTimeOffset? LastModified,
    string? PublicAccess,
    string? LeaseStatus);

/// <summary>
/// Represents either a real blob (IsPrefix = false) or a virtual folder prefix
/// (IsPrefix = true). When IsPrefix = true, SizeBytes / ContentType / LastModified
/// / ETag are all null — only Name (the prefix string) is meaningful.
/// </summary>
public sealed record StorageBlobItem(
    string Name,
    bool IsPrefix,
    long? SizeBytes,
    string? ContentType,
    DateTimeOffset? LastModified,
    string? ETag);

public sealed record StorageBlobPage(
    IReadOnlyList<StorageBlobItem> Items,
    string? ContinuationToken);

public sealed record BlobProperties(
    string Name,
    long SizeBytes,
    string ContentType,
    DateTimeOffset LastModified,
    string ETag,
    string? LeaseStatus,
    string? LeaseState,
    string? AccessTier,
    bool? AccessTierInferred,
    string? ContentEncoding,
    string? ContentLanguage,
    string? CacheControl,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyDictionary<string, string> Tags);

public sealed record StorageBlobContent(
    string ContainerName,
    string BlobName,
    string Content,
    string? ContentType,
    long TotalSizeBytes,
    bool WasTruncated,
    bool IsBinary);
