using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using AzureBlobProperties = Azure.Storage.Blobs.Models.BlobProperties;
using BlobDownloadOptions = Azure.Storage.Blobs.Models.BlobDownloadOptions;
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

        if (!IsTextContentType(props.ContentType))
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
            text = result.Value.Content.ToString();
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
        CancellationToken ct = default)
    {
        var blobClient = _blobService.GetBlobContainerClient(containerName).GetBlobClient(blobName);
        await blobClient.DownloadToAsync(destination, cancellationToken: ct);
    }

    private static bool IsTextContentType(string? contentType)
    {
        if (contentType is null) return false;
        var normalized = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return normalized.StartsWith("text/", StringComparison.Ordinal)
            || normalized is "application/json" or "application/xml" or "application/x-www-form-urlencoded";
    }
}
