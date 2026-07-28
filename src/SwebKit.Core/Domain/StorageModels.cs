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

public sealed record BlobVersionItem(
    string VersionId,
    DateTimeOffset? CreatedOn,
    long? ContentLength,
    bool IsCurrentVersion);

// --- Mutation capability and operation models ---

public record StorageCapabilities(
    bool VersioningEnabled,
    bool SoftDeleteEnabled,
    bool CanUpload,
    bool CanCopy,
    bool CanSetMetadata,
    bool CanRestore);

public record BlobUploadOptions(
    string ContainerName,
    string BlobName,
    bool Overwrite,
    string? ContentType = null);

public record BlobCopyOptions(
    string SourceContainer,
    string SourceBlobName,
    string DestinationContainer,
    string DestinationBlobName,
    string? SourceVersionId = null,
    bool Overwrite = false);

public record BlobMutationResult(
    bool Success,
    string? ErrorMessage = null,
    string? ResultBlobPath = null);

public record BlobMetadataDiff(
    IReadOnlyDictionary<string, string?> Before,
    IReadOnlyDictionary<string, string?> After,
    IReadOnlyList<string> AddedKeys,
    IReadOnlyList<string> RemovedKeys,
    IReadOnlyList<string> ChangedKeys)
{
    public static BlobMetadataDiff Compute(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        var beforeNullable = before.ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value);
        var afterNullable = after.ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value);
        var allKeys = beforeNullable.Keys.Union(afterNullable.Keys).ToList();
        List<string> added = [.. allKeys.Where(k => !beforeNullable.ContainsKey(k))];
        List<string> removed = [.. allKeys.Where(k => !afterNullable.ContainsKey(k))];
        List<string> changed = [.. allKeys.Where(k => beforeNullable.ContainsKey(k)
                                                    && afterNullable.ContainsKey(k)
                                                    && beforeNullable[k] != afterNullable[k])];
        return new BlobMetadataDiff(beforeNullable, afterNullable, added, removed, changed);
    }
}

public record BlobVersionComparison(
    string BaseVersionId,
    string? CompareVersionId,
    BlobMetadataDiff MetadataDiff,
    bool ContentComparePossible,
    long? BaseSizeBytes,
    long? CompareSizeBytes,
    string? TextDiff);

public enum BlobRecoveryState { Restored, Undeleted, Unsupported, Failed }

public record BlobRecoveryResult(
    BlobRecoveryState State,
    string? ResultBlobPath = null,
    string? ErrorMessage = null);

public record DeletedBlobItem(
    string Name,
    DateTimeOffset DeletedOn,
    int RemainingDays);
