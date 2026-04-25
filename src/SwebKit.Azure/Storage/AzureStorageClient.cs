using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using AzureBlobProperties = Azure.Storage.Blobs.Models.BlobProperties;
using AzureBlobUploadOptions = Azure.Storage.Blobs.Models.BlobUploadOptions;
using BlobDownloadOptions = Azure.Storage.Blobs.Models.BlobDownloadOptions;
using BlobDownloadToOptions = Azure.Storage.Blobs.Models.BlobDownloadToOptions;
using BlobHttpHeaders = Azure.Storage.Blobs.Models.BlobHttpHeaders;
using BlobRequestConditions = Azure.Storage.Blobs.Models.BlobRequestConditions;
using BlobStates = Azure.Storage.Blobs.Models.BlobStates;
using BlobTraits = Azure.Storage.Blobs.Models.BlobTraits;

namespace SwebKit.Azure.Storage;

public class AzureStorageClient : IStorageClient
{
    private readonly BlobServiceClient _blobService;

    public StorageConfig Config { get; }

    public AzureStorageClient(StorageConfig config, ICredentialStore credentialStore)
    {
        Config = config;

        if (!config.UseAad && config.ConnectionStringRef is not null)
        {
            var connStr = credentialStore.Get(config.ConnectionStringRef)
                ?? throw new InvalidOperationException($"Credential '{config.ConnectionStringRef}' not found.");
            _blobService = new BlobServiceClient(connStr);
        }
        else if (config.UseAad && !string.IsNullOrEmpty(config.AccountName))
        {
            _blobService = new BlobServiceClient(
                new Uri($"https://{config.AccountName}.blob.core.windows.net"),
                new DefaultAzureCredential());
        }
        else
        {
            throw new InvalidOperationException(
                "Invalid StorageConfig: set UseAad=true with a non-empty AccountName, " +
                "or set UseAad=false with a non-null ConnectionStringRef.");
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        await foreach (var _ in _blobService.GetBlobContainersAsync(cancellationToken: ct))
            break;
        return true;
    }

    public async Task<IReadOnlyList<StorageContainerItem>> ListContainersAsync(CancellationToken ct = default)
    {
        var result = new List<StorageContainerItem>();
        await foreach (var item in _blobService.GetBlobContainersAsync(cancellationToken: ct))
        {
            result.Add(new StorageContainerItem(
                Name: item.Name,
                LastModified: item.Properties.LastModified,
                PublicAccess: item.Properties.PublicAccess?.ToString(),
                LeaseStatus: item.Properties.LeaseStatus?.ToString()));
        }
        return result;
    }

    public async Task<StorageBlobPage> ListBlobsAsync(
        string containerName,
        string prefix,
        string? continuationToken = null,
        int pageSize = 100,
        CancellationToken ct = default)
    {
        var container = _blobService.GetBlobContainerClient(containerName);
        var items = new List<StorageBlobItem>();
        string? nextToken = null;

        await foreach (var page in container
            .GetBlobsByHierarchyAsync(BlobTraits.None, BlobStates.None, "/", prefix, ct)
            .AsPages(continuationToken, pageSize))
        {
            foreach (var item in page.Values)
            {
                if (item.IsPrefix)
                {
                    items.Add(new StorageBlobItem(item.Prefix, true, null, null, null, null));
                }
                else
                {
                    items.Add(new StorageBlobItem(
                        Name: item.Blob.Name,
                        IsPrefix: false,
                        SizeBytes: item.Blob.Properties.ContentLength,
                        ContentType: item.Blob.Properties.ContentType,
                        LastModified: item.Blob.Properties.LastModified,
                        ETag: item.Blob.Properties.ETag?.ToString()));
                }
            }
            nextToken = page.ContinuationToken;
            break; // only first page
        }

        return new StorageBlobPage(items, string.IsNullOrEmpty(nextToken) ? null : nextToken);
    }

    public async Task<BlobProperties> GetBlobPropertiesAsync(
        string containerName,
        string blobName,
        CancellationToken ct = default)
    {
        var blobClient = _blobService.GetBlobContainerClient(containerName).GetBlobClient(blobName);
        var propsResponse = await blobClient.GetPropertiesAsync(cancellationToken: ct);
        AzureBlobProperties props = propsResponse.Value;

        Dictionary<string, string> tags;
        try
        {
            var tagsResponse = await blobClient.GetTagsAsync(cancellationToken: ct);
            tags = new Dictionary<string, string>(tagsResponse.Value.Tags);
        }
        catch (RequestFailedException ex) when (ex.Status == 403)
        {
            // Account may have disallowed blob index tags for this credential role.
            tags = new Dictionary<string, string>();
        }

        return new BlobProperties(
            Name: blobName,
            SizeBytes: props.ContentLength,
            ContentType: props.ContentType,
            LastModified: props.LastModified,
            ETag: props.ETag.ToString(),
            LeaseStatus: props.LeaseStatus.ToString(),
            LeaseState: props.LeaseState.ToString(),
            AccessTier: props.AccessTier?.ToString(),
            AccessTierInferred: props.AccessTierInferred,
            ContentEncoding: props.ContentEncoding,
            ContentLanguage: props.ContentLanguage,
            CacheControl: props.CacheControl,
            Metadata: new Dictionary<string, string>(props.Metadata),
            Tags: new Dictionary<string, string>(tags));
    }

    public async Task<StorageBlobContent> GetBlobContentAsync(
        string containerName,
        string blobName,
        int maxBytes = 524_288,
        CancellationToken ct = default)
    {
        var blobClient = _blobService.GetBlobContainerClient(containerName).GetBlobClient(blobName);
        var propsResponse = await blobClient.GetPropertiesAsync(cancellationToken: ct);
        AzureBlobProperties props = propsResponse.Value;

        if (!IsTextContentType(props.ContentType, blobName))
        {
            return new StorageBlobContent(
                ContainerName: containerName,
                BlobName: blobName,
                Content: string.Empty,
                ContentType: props.ContentType,
                TotalSizeBytes: props.ContentLength,
                WasTruncated: false,
                IsBinary: true);
        }

        bool wasTruncated = props.ContentLength > maxBytes;
        string text;

        if (wasTruncated)
        {
            var options = new BlobDownloadOptions { Range = new HttpRange(0, maxBytes) };
            var streamResponse = await blobClient.DownloadStreamingAsync(options, ct);
            using var streamResult = streamResponse.Value;
            using var ms = new MemoryStream();
            await streamResult.Content.CopyToAsync(ms, ct);
            text = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        else
        {
            var result = await blobClient.DownloadContentAsync(cancellationToken: ct);
            text = System.Text.Encoding.UTF8.GetString(result.Value.Content.ToArray());
        }

        return new StorageBlobContent(
            ContainerName: containerName,
            BlobName: blobName,
            Content: text,
            ContentType: props.ContentType,
            TotalSizeBytes: props.ContentLength,
            WasTruncated: wasTruncated,
            IsBinary: false);
    }

    public Task<string> GetBlobSasUrlAsync(
        string containerName,
        string blobName,
        TimeSpan expiry,
        CancellationToken ct = default)
    {
        var blobClient = _blobService.GetBlobContainerClient(containerName).GetBlobClient(blobName);
        var uri = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(expiry));
        return Task.FromResult(uri.ToString());
    }

    public async Task DownloadBlobAsync(
        string containerName,
        string blobName,
        Stream destination,
        IProgress<long>? progress = null,
        string? versionId = null,
        CancellationToken ct = default)
    {
        var blobClient = _blobService.GetBlobContainerClient(containerName).GetBlobClient(blobName);
        if (!string.IsNullOrWhiteSpace(versionId))
            blobClient = blobClient.WithVersion(versionId);

        var options = new BlobDownloadToOptions
        {
            ProgressHandler = progress
        };

        await blobClient.DownloadToAsync(destination, options, ct);
    }

    public async Task<IReadOnlyList<BlobVersionItem>> ListBlobVersionsAsync(
        string containerName, string blobName, CancellationToken ct = default)
    {
        var container = _blobService.GetBlobContainerClient(containerName);
        var result = new List<BlobVersionItem>();

        await foreach (var item in container.GetBlobsAsync(
            BlobTraits.None, BlobStates.Version, blobName, ct))
        {
            if (!IsExactVersionMatch(blobName, item.Name))
            {
                continue;
            }

            result.Add(new BlobVersionItem(
                VersionId: item.VersionId ?? string.Empty,
                CreatedOn: item.Properties.CreatedOn,
                ContentLength: item.Properties.ContentLength,
                IsCurrentVersion: item.IsLatestVersion ?? false));
        }

        return result;
    }

    internal static bool IsExactVersionMatch(string blobName, string? candidateBlobName) =>
        string.Equals(blobName, candidateBlobName, StringComparison.Ordinal);

    public Task<string> GetContainerSasUrlAsync(
        string containerName, TimeSpan expiry, CancellationToken ct = default)
    {
        var container = _blobService.GetBlobContainerClient(containerName);
        var uri = container.GenerateSasUri(
            BlobContainerSasPermissions.Read | BlobContainerSasPermissions.List,
            DateTimeOffset.UtcNow.Add(expiry));
        return Task.FromResult(uri.ToString());
    }

    public async Task<StorageCapabilities> GetStorageCapabilitiesAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _blobService.GetPropertiesAsync(cancellationToken: ct);
            var props = response.Value;
            // The data-plane BlobServiceProperties (Azure.Storage.Blobs 12.x) does not expose
            // account-level versioning. Keep soft-delete detection accurate, but treat version
            // workflows as available and let per-blob list/restore calls determine actual support.
            bool softDelete = props.DeleteRetentionPolicy?.Enabled == true;
            return new StorageCapabilities(
                VersioningEnabled: true,
                SoftDeleteEnabled: softDelete,
                CanUpload: true,
                CanCopy: true,
                CanSetMetadata: true,
                CanRestore: true);
        }
        catch (OperationCanceledException) { throw; }
        catch (RequestFailedException)
        {
            return new StorageCapabilities(false, false, false, false, false, false);
        }
    }

    public async Task<BlobMutationResult> UploadBlobAsync(
        BlobUploadOptions options, Stream source, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var blobClient = _blobService.GetBlobContainerClient(options.ContainerName).GetBlobClient(options.BlobName);
            var azureOptions = new AzureBlobUploadOptions { ProgressHandler = progress };
            if (options.ContentType is not null)
                azureOptions.HttpHeaders = new BlobHttpHeaders { ContentType = options.ContentType };
            if (!options.Overwrite)
                azureOptions.Conditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") };

            await blobClient.UploadAsync(source, azureOptions, ct);
            return new BlobMutationResult(true, ResultBlobPath: $"{options.ContainerName}/{options.BlobName}");
        }
        catch (OperationCanceledException) { throw; }
        catch (RequestFailedException ex)
        {
            return new BlobMutationResult(false, ErrorMessage: ex.Message);
        }
    }

    public async Task<BlobMutationResult> CopyBlobAsync(BlobCopyOptions options, CancellationToken ct = default)
    {
        try
        {
            var srcBlob = _blobService.GetBlobContainerClient(options.SourceContainer).GetBlobClient(options.SourceBlobName);
            if (!string.IsNullOrWhiteSpace(options.SourceVersionId))
                srcBlob = srcBlob.WithVersion(options.SourceVersionId);

            var destBlob = _blobService.GetBlobContainerClient(options.DestinationContainer).GetBlobClient(options.DestinationBlobName);
            if (options.Overwrite)
                await destBlob.DeleteIfExistsAsync(cancellationToken: ct);

            var operation = await destBlob.StartCopyFromUriAsync(srcBlob.Uri, cancellationToken: ct);
            await operation.WaitForCompletionAsync(ct);
            return new BlobMutationResult(true, ResultBlobPath: $"{options.DestinationContainer}/{options.DestinationBlobName}");
        }
        catch (OperationCanceledException) { throw; }
        catch (RequestFailedException ex)
        {
            return new BlobMutationResult(false, ErrorMessage: ex.Message);
        }
    }

    public async Task<BlobMutationResult> SetBlobMetadataAsync(
        string containerName, string blobName, IDictionary<string, string> metadata,
        string? ifMatchEtag = null, CancellationToken ct = default)
    {
        try
        {
            var blobClient = _blobService.GetBlobContainerClient(containerName).GetBlobClient(blobName);
            BlobRequestConditions? conditions = ifMatchEtag is not null
                ? new BlobRequestConditions { IfMatch = new ETag(ifMatchEtag) }
                : null;
            await blobClient.SetMetadataAsync(metadata, conditions, ct);
            return new BlobMutationResult(true, ResultBlobPath: $"{containerName}/{blobName}");
        }
        catch (OperationCanceledException) { throw; }
        catch (RequestFailedException ex)
        {
            return new BlobMutationResult(false, ErrorMessage: ex.Message);
        }
    }

    public async Task<BlobVersionComparison> GetVersionComparisonAsync(
        string containerName, string blobName, string baseVersionId,
        string? compareVersionId = null, CancellationToken ct = default)
    {
        var container = _blobService.GetBlobContainerClient(containerName);

        var basePropsResponse = await container.GetBlobClient(blobName).WithVersion(baseVersionId).GetPropertiesAsync(cancellationToken: ct);
        var baseProps = basePropsResponse.Value;

        AzureBlobProperties compareProps;
        if (compareVersionId is not null)
        {
            var r = await container.GetBlobClient(blobName).WithVersion(compareVersionId).GetPropertiesAsync(cancellationToken: ct);
            compareProps = r.Value;
        }
        else
        {
            var r = await container.GetBlobClient(blobName).GetPropertiesAsync(cancellationToken: ct);
            compareProps = r.Value;
        }

        var diff = BlobMetadataDiff.Compute(
            new Dictionary<string, string>(baseProps.Metadata),
            new Dictionary<string, string>(compareProps.Metadata));

        const long maxTextBytes = 100 * 1024;
        bool contentComparePossible = false;
        string? textDiff = null;

        bool bothSmall = baseProps.ContentLength <= maxTextBytes && compareProps.ContentLength <= maxTextBytes;
        bool baseIsText = IsTextContentType(baseProps.ContentType, blobName);
        bool compareIsText = IsTextContentType(compareProps.ContentType, blobName);

        if (bothSmall && baseIsText && compareIsText)
        {
            try
            {
                using var baseMs = new MemoryStream();
                await DownloadBlobAsync(containerName, blobName, baseMs, null, baseVersionId, ct);
                var baseText = System.Text.Encoding.UTF8.GetString(baseMs.ToArray());

                using var compareMs = new MemoryStream();
                await DownloadBlobAsync(containerName, blobName, compareMs, null, compareVersionId, ct);
                var compareText = System.Text.Encoding.UTF8.GetString(compareMs.ToArray());

                contentComparePossible = true;
                textDiff = ProduceSimpleLineDiff(baseText, compareText);
            }
            catch (OperationCanceledException) { throw; }
            catch (RequestFailedException ex)
            {
                contentComparePossible = false;
                textDiff = $"Content download failed: {ex.Message}";
            }
        }
        else
        {
            textDiff = !bothSmall ? "Content too large for text comparison (> 100 KB)" : "Non-text content type";
        }

        return new BlobVersionComparison(
            BaseVersionId: baseVersionId,
            CompareVersionId: compareVersionId,
            MetadataDiff: diff,
            ContentComparePossible: contentComparePossible,
            BaseSizeBytes: baseProps.ContentLength,
            CompareSizeBytes: compareProps.ContentLength,
            TextDiff: textDiff);
    }

    public async Task<BlobRecoveryResult> RestoreBlobVersionAsync(
        string containerName, string blobName, string versionId, CancellationToken ct = default)
    {
        try
        {
            var container = _blobService.GetBlobContainerClient(containerName);
            var versionedBlob = container.GetBlobClient(blobName).WithVersion(versionId);
            var currentBlob = container.GetBlobClient(blobName);

            var operation = await currentBlob.StartCopyFromUriAsync(versionedBlob.Uri, cancellationToken: ct);
            await operation.WaitForCompletionAsync(ct);
            return new BlobRecoveryResult(BlobRecoveryState.Restored, ResultBlobPath: $"{containerName}/{blobName}");
        }
        catch (OperationCanceledException) { throw; }
        catch (RequestFailedException ex)
        {
            return new BlobRecoveryResult(BlobRecoveryState.Failed, ErrorMessage: ex.Message);
        }
    }

    public async Task<BlobRecoveryResult> UndeleteBlobAsync(
        string containerName, string blobName, CancellationToken ct = default)
    {
        try
        {
            var blobClient = _blobService.GetBlobContainerClient(containerName).GetBlobClient(blobName);
            await blobClient.UndeleteAsync(ct);
            return new BlobRecoveryResult(BlobRecoveryState.Undeleted, ResultBlobPath: $"{containerName}/{blobName}");
        }
        catch (OperationCanceledException) { throw; }
        catch (RequestFailedException ex) when (ex.ErrorCode is "BlobNotFound" or "BlobSoftDeleteNotEnabled")
        {
            return new BlobRecoveryResult(BlobRecoveryState.Unsupported, ErrorMessage: ex.Message);
        }
        catch (RequestFailedException ex)
        {
            return new BlobRecoveryResult(BlobRecoveryState.Failed, ErrorMessage: ex.Message);
        }
    }

    private static string ProduceSimpleLineDiff(string baseText, string compareText)
    {
        var baseLines = baseText.Split('\n');
        var compareLines = compareText.Split('\n');
        int maxLines = Math.Max(baseLines.Length, compareLines.Length);
        var diffLines = new List<string>();

        for (int i = 0; i < maxLines; i++)
        {
            var b = i < baseLines.Length ? baseLines[i] : null;
            var c = i < compareLines.Length ? compareLines[i] : null;
            if (b == c) continue;
            if (b is not null && c is null) diffLines.Add($"- {b}");
            else if (b is null && c is not null) diffLines.Add($"+ {c}");
            else { diffLines.Add($"- {b}"); diffLines.Add($"+ {c}"); }
        }

        return diffLines.Count > 0 ? string.Join("\n", diffLines) : "(no content differences)";
    }

    /// <summary>
    /// Returns false only for clearly binary content types.
    /// Null/empty content type (common for blobs uploaded without explicit type) defaults to
    /// attempting a text preview, matching the Azure Portal behaviour.
    /// When content type is the generic "application/octet-stream", falls back to the file
    /// extension of <paramref name="blobName"/> so that .txt/.log/.json/etc. files are still
    /// shown as text even if the blob was uploaded without an explicit content type.
    /// </summary>
    internal static bool IsTextContentType(string? contentType, string? blobName = null)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return true; // no content type set — attempt text preview

        var normalized = contentType.Split(';')[0].Trim().ToLowerInvariant();

        // Explicit text types
        if (normalized.StartsWith("text/", StringComparison.Ordinal))
            return true;
        if (normalized is "application/json" or "application/xml" or "application/x-www-form-urlencoded"
                       or "application/javascript" or "application/x-javascript"
                       or "application/yaml" or "application/x-yaml")
            return true;

        // Clearly binary: images, video, audio
        if (normalized.StartsWith("image/", StringComparison.Ordinal)
            || normalized.StartsWith("video/", StringComparison.Ordinal)
            || normalized.StartsWith("audio/", StringComparison.Ordinal))
            return false;

        // Clearly binary: common binary application types
        if (normalized is "application/zip" or "application/gzip"
                       or "application/x-zip-compressed" or "application/x-tar" or "application/x-gzip"
                       or "application/pdf"
                       or "application/vnd.ms-excel"
                       or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                       or "application/msword"
                       or "application/vnd.openxmlformats-officedocument.wordprocessingml.document")
            return false;

        // "application/octet-stream" is a generic default often set by uploaders that don't
        // know the real type.  Fall back to the file extension before declaring it binary.
        if (normalized is "application/octet-stream")
            return HasTextExtension(blobName);

        // Unknown type — attempt text preview (worst case shows garbled content)
        return true;
    }

    /// <summary>
    /// Returns true when the blob name's extension is a well-known plain-text format.
    /// </summary>
    private static bool HasTextExtension(string? blobName)
    {
        if (string.IsNullOrEmpty(blobName)) return false;
        var ext = Path.GetExtension(blobName).ToLowerInvariant();
        return ext is ".txt" or ".log" or ".json" or ".xml" or ".csv"
                    or ".yaml" or ".yml" or ".md" or ".html" or ".htm"
                    or ".js" or ".ts" or ".css" or ".sql" or ".cs"
                    or ".py" or ".sh" or ".ps1" or ".ini" or ".cfg"
                    or ".conf" or ".toml" or ".jsx" or ".tsx"
                    or ".java" or ".go" or ".rs" or ".rb" or ".php"
                    or ".vue" or ".svelte";
    }
}
