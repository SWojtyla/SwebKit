using System.Text;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// In-memory Storage client that returns realistic synthetic data for demo/testing.
/// Simulates 2 accounts, each with 3 containers and a mix of blobs.
/// </summary>
public sealed class DemoStorageClient : IStorageClient
{
    private static readonly StorageConfig DemoConfig = new()
    {
        Id = "demo-storage",
        DisplayName = "Demo Storage",
        AccountName = "devstore",
        UseAad = false
    };

    public StorageConfig Config => DemoConfig;

    private static readonly IReadOnlyList<StorageContainerItem> Containers =
    [
        new("configs", DateTimeOffset.UtcNow.AddDays(-30), "None", "unlocked"),
        new("exports", DateTimeOffset.UtcNow.AddDays(-10), "None", "unlocked"),
        new("fixtures", DateTimeOffset.UtcNow.AddDays(-60), "None", "unlocked")
    ];

    private readonly Dictionary<string, List<StorageBlobItem>> _blobsByContainer;
    private readonly Dictionary<string, string> _blobContents;
    private readonly Dictionary<string, Dictionary<string, string>> _blobMetadata;
    private readonly Dictionary<string, List<DeletedBlobItem>> _deletedBlobsByContainer;

    public DemoStorageClient()
    {
        _blobsByContainer = new(StringComparer.OrdinalIgnoreCase)
        {
            ["configs"] =
            [
                new("app-settings.json", false, 842, "application/json", DateTimeOffset.UtcNow.AddDays(-2), "\"etag-cfg-001\""),
                new("feature-flags.json", false, 312, "application/json", DateTimeOffset.UtcNow.AddDays(-5), "\"etag-cfg-002\""),
                new("rate-limits.json", false, 198, "application/json", DateTimeOffset.UtcNow.AddDays(-7), "\"etag-cfg-003\""),
                new("env/", true, null, null, null, null),
            ],
            ["configs/env/"] =
            [
                new("env/prod.json", false, 1024, "application/json", DateTimeOffset.UtcNow.AddDays(-1), "\"etag-env-001\""),
                new("env/staging.json", false, 998, "application/json", DateTimeOffset.UtcNow.AddDays(-3), "\"etag-env-002\""),
            ],
            ["exports"] =
            [
                new("2026-03-21-report.csv", false, 15_420, "text/csv", DateTimeOffset.UtcNow.AddDays(-1), "\"etag-exp-001\""),
                new("2026-03-14-report.csv", false, 14_882, "text/csv", DateTimeOffset.UtcNow.AddDays(-8), "\"etag-exp-002\""),
                new("2026-03-07-report.csv", false, 16_011, "text/csv", DateTimeOffset.UtcNow.AddDays(-15), "\"etag-exp-003\""),
                new("archive/", true, null, null, null, null),
            ],
            ["exports/archive/"] =
            [
                new("archive/2026-01-report.csv", false, 13_200, "text/csv", DateTimeOffset.UtcNow.AddDays(-80), "\"etag-arc-001\""),
            ],
            ["fixtures"] =
            [
                new("test-payload.json", false, 512, "application/json", DateTimeOffset.UtcNow.AddDays(-30), "\"etag-fix-001\""),
                new("seed-data.json", false, 2_048, "application/json", DateTimeOffset.UtcNow.AddDays(-30), "\"etag-fix-002\""),
                new("mock-orders.json", false, 4_096, "application/json", DateTimeOffset.UtcNow.AddDays(-30), "\"etag-fix-003\""),
                new("sample-report.csv", false, 8_192, "text/csv", DateTimeOffset.UtcNow.AddDays(-30), "\"etag-fix-004\""),
                new("readme.txt", false, 256, "text/plain", DateTimeOffset.UtcNow.AddDays(-30), "\"etag-fix-005\""),
            ]
        };

        _blobContents = new(StringComparer.OrdinalIgnoreCase)
        {
            ["configs/app-settings.json"] = """
                {
                  "Logging": { "LogLevel": { "Default": "Information", "Microsoft": "Warning" } },
                  "ConnectionStrings": { "DefaultDb": "Server=db.internal;Database=ecommerce;..." },
                  "FeatureFlags": { "NewCheckout": true, "BetaApi": false },
                  "Limits": { "MaxRequestSize": 10485760, "RateLimitPerMinute": 100 }
                }
                """,
            ["configs/feature-flags.json"] = """
                {
                  "flags": [
                    { "name": "new-checkout", "enabled": true, "rollout": 100 },
                    { "name": "beta-api", "enabled": false, "rollout": 0 },
                    { "name": "dark-mode", "enabled": true, "rollout": 50 }
                  ]
                }
                """,
            ["configs/rate-limits.json"] = """
                {
                  "api": { "perMinute": 100, "perHour": 3000 },
                  "auth": { "perMinute": 10, "perHour": 200 },
                  "search": { "perMinute": 60, "perHour": 1200 }
                }
                """,
            ["configs/env/prod.json"] = """
                {
                  "Environment": "Production",
                  "ServiceBus": { "Namespace": "orders.servicebus.windows.net" },
                  "Redis": { "Endpoint": "redis.internal:6379", "Ssl": true },
                  "Logging": { "Level": "Warning", "Sinks": ["ApplicationInsights"] }
                }
                """,
            ["configs/env/staging.json"] = """
                {
                  "Environment": "Staging",
                  "ServiceBus": { "Namespace": "orders-staging.servicebus.windows.net" },
                  "Redis": { "Endpoint": "redis-staging.internal:6379", "Ssl": false },
                  "Logging": { "Level": "Information", "Sinks": ["Console", "ApplicationInsights"] }
                }
                """,
            ["exports/2026-03-21-report.csv"] = "OrderId,Customer,Amount,Status,Date\nORD-12345,C-1042,99.99,Fulfilled,2026-03-21\nORD-12346,C-2087,149.99,Shipped,2026-03-21\nORD-12347,C-0519,34.50,Pending,2026-03-21\n",
            ["exports/2026-03-14-report.csv"] = "OrderId,Customer,Amount,Status,Date\nORD-12200,C-0888,55.00,Fulfilled,2026-03-14\nORD-12201,C-1999,220.00,Fulfilled,2026-03-14\n",
            ["exports/2026-03-07-report.csv"] = "OrderId,Customer,Amount,Status,Date\nORD-12100,C-3410,79.90,Cancelled,2026-03-07\nORD-12101,C-4521,19.99,Fulfilled,2026-03-07\n",
            ["exports/archive/2026-01-report.csv"] = "OrderId,Customer,Amount,Status,Date\nORD-11001,C-0100,30.00,Fulfilled,2026-01-15\n",
            ["fixtures/test-payload.json"] = """
                {
                  "event": "order.created",
                  "orderId": "ORD-TEST-001",
                  "customer": { "id": "C-0001", "email": "test@example.com" },
                  "items": [{ "sku": "SKU-0001", "qty": 1, "price": 9.99 }],
                  "total": 9.99,
                  "timestamp": "2026-01-01T00:00:00Z"
                }
                """,
            ["fixtures/seed-data.json"] = """
                {
                  "customers": [
                    { "id": "C-0001", "name": "Alice Test", "email": "alice@test.example.com" },
                    { "id": "C-0002", "name": "Bob Test", "email": "bob@test.example.com" }
                  ],
                  "products": [
                    { "sku": "SKU-0001", "name": "Widget A", "price": 9.99 },
                    { "sku": "SKU-0002", "name": "Widget B", "price": 19.99 }
                  ]
                }
                """,
            ["fixtures/mock-orders.json"] = """
                [
                  { "orderId": "ORD-MOCK-001", "customerId": "C-0001", "total": 9.99, "status": "pending" },
                  { "orderId": "ORD-MOCK-002", "customerId": "C-0002", "total": 19.99, "status": "fulfilled" },
                  { "orderId": "ORD-MOCK-003", "customerId": "C-0001", "total": 29.98, "status": "shipped" }
                ]
                """,
            ["fixtures/sample-report.csv"] = "Id,Name,Value\n1,Alpha,100\n2,Beta,200\n3,Gamma,300\n",
            ["fixtures/readme.txt"] = "This container holds fixture data for automated testing.\nDo not use in production.\n"
        };

        _blobMetadata = new(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _blobContents.Keys)
        {
            _blobMetadata[key] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["demo"] = "true" };
        }

        _deletedBlobsByContainer = new(StringComparer.OrdinalIgnoreCase)
        {
            ["configs"] =
            [
                new DeletedBlobItem("deleted-config.json", DateTimeOffset.UtcNow.AddDays(-2), 11),
            ],
            ["exports"] =
            [
                new DeletedBlobItem("deleted-report.csv", DateTimeOffset.UtcNow.AddDays(-1), 12),
            ],
            ["fixtures"] =
            [],
        };
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<IReadOnlyList<StorageContainerItem>> ListContainersAsync(CancellationToken ct = default) =>
        Task.FromResult(Containers);

    public Task<StorageBlobPage> ListBlobsAsync(
        string containerName, string prefix, string? continuationToken = null,
        int pageSize = 100, CancellationToken ct = default)
    {
        var key = string.IsNullOrEmpty(prefix) ? containerName : $"{containerName}/{prefix}";
        var items = _blobsByContainer.TryGetValue(key, out var blobs) ? blobs : [];
        return Task.FromResult(new StorageBlobPage(items, null));
    }

    public Task<BlobProperties> GetBlobPropertiesAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        var key = $"{containerName}/{blobName}";
        var allBlobs = _blobsByContainer.Values.SelectMany(b => b).Where(b => !b.IsPrefix).ToList();
        var blob = allBlobs.FirstOrDefault(b => string.Equals($"{containerName}/{b.Name}", key, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(new BlobProperties(
            blobName,
            blob?.SizeBytes ?? 0,
            blob?.ContentType ?? "application/octet-stream",
            blob?.LastModified ?? DateTimeOffset.UtcNow,
            blob?.ETag ?? "\"demo-etag\"",
            "unlocked",
            "available",
            "Hot",
            false,
            null, null, null,
            new Dictionary<string, string>(_blobMetadata.GetValueOrDefault(key) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["demo"] = "true" }),
            new Dictionary<string, string>()));
    }

    public Task<StorageBlobContent> GetBlobContentAsync(
        string containerName, string blobName, int maxBytes = 524_288, CancellationToken ct = default)
    {
        var key = $"{containerName}/{blobName}";
        var content = _blobContents.TryGetValue(key, out var c) ? c : $"(Demo content for {blobName})";
        var bytes = Encoding.UTF8.GetByteCount(content);
        var isBinary = false;

        return Task.FromResult(new StorageBlobContent(
            containerName, blobName, content,
            blobName.EndsWith(".json", StringComparison.Ordinal) ? "application/json"
                : blobName.EndsWith(".csv", StringComparison.Ordinal) ? "text/csv"
                : "text/plain",
            bytes, false, isBinary));
    }

    public Task<string> GetBlobSasUrlAsync(
        string containerName, string blobName, TimeSpan expiry, CancellationToken ct = default) =>
        Task.FromResult($"https://devstore.blob.core.windows.net/{containerName}/{blobName}?sv=demo&se={DateTimeOffset.UtcNow.Add(expiry):O}&sp=r");

    public Task DownloadBlobAsync(
        string containerName,
        string blobName,
        Stream destination,
        IProgress<long>? progress = null,
        string? versionId = null,
        CancellationToken ct = default)
    {
        var key = $"{containerName}/{blobName}";
        var content = _blobContents.TryGetValue(key, out var c) ? c : $"(Demo content for {blobName})";
        var bytes = Encoding.UTF8.GetBytes(content);

        return WriteAsync(destination, bytes, progress, ct);
    }

    public Task<IReadOnlyList<BlobVersionItem>> ListBlobVersionsAsync(
        string containerName, string blobName, CancellationToken ct = default)
    {
        IReadOnlyList<BlobVersionItem> versions =
        [
            new("2026-03-20T10:00:00Z", DateTimeOffset.UtcNow.AddDays(-1), 1024, true),
            new("2026-03-15T08:30:00Z", DateTimeOffset.UtcNow.AddDays(-6), 980, false),
            new("2026-03-10T14:15:00Z", DateTimeOffset.UtcNow.AddDays(-11), 950, false),
        ];
        return Task.FromResult(versions);
    }

    public Task<string> GetContainerSasUrlAsync(
        string containerName, TimeSpan expiry, CancellationToken ct = default) =>
        Task.FromResult($"https://devstore.blob.core.windows.net/{containerName}?sv=demo&se={DateTimeOffset.UtcNow.Add(expiry):O}&sp=rl");

    public Task<StorageCapabilities> GetStorageCapabilitiesAsync(CancellationToken ct = default) =>
        Task.FromResult(new StorageCapabilities(
            VersioningEnabled: true,
            SoftDeleteEnabled: true,
            CanUpload: true,
            CanCopy: true,
            CanSetMetadata: true,
            CanRestore: true));

    public Task<BlobMutationResult> UploadBlobAsync(
        BlobUploadOptions options, Stream source, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        var key = $"{options.ContainerName}/{options.BlobName}";
        using var reader = new StreamReader(source, Encoding.UTF8, leaveOpen: true);
        var content = reader.ReadToEnd();
        _blobContents[key] = content;
        var bytes = Encoding.UTF8.GetByteCount(content);

        var list = _blobsByContainer.GetValueOrDefault(options.ContainerName);
        if (list is null)
        {
            list = [];
            _blobsByContainer[options.ContainerName] = list;
        }

        var existing = list.FindIndex(b => string.Equals(b.Name, options.BlobName, StringComparison.OrdinalIgnoreCase));
        var newItem = new StorageBlobItem(options.BlobName, false, bytes, options.ContentType ?? "text/plain", DateTimeOffset.UtcNow, "\"demo-etag-new\"");
        if (existing >= 0)
        {
            list[existing] = newItem;
        }
        else
        {
            list.Add(newItem);
        }

        progress?.Report(bytes);
        return Task.FromResult(new BlobMutationResult(true, ResultBlobPath: key));
    }

    public Task<BlobMutationResult> CopyBlobAsync(BlobCopyOptions options, CancellationToken ct = default)
    {
        var sourceKey = $"{options.SourceContainer}/{options.SourceBlobName}";
        if (!_blobContents.TryGetValue(sourceKey, out var content))
        {
            return Task.FromResult(new BlobMutationResult(false, ErrorMessage: $"Source blob not found: {sourceKey}"));
        }

        var destKey = $"{options.DestinationContainer}/{options.DestinationBlobName}";
        _blobContents[destKey] = content;
        var bytes = Encoding.UTF8.GetByteCount(content);

        var destList = _blobsByContainer.GetValueOrDefault(options.DestinationContainer);
        if (destList is null)
        {
            destList = [];
            _blobsByContainer[options.DestinationContainer] = destList;
        }

        var contentType = options.DestinationBlobName.EndsWith(".json", StringComparison.Ordinal) ? "application/json"
            : options.DestinationBlobName.EndsWith(".csv", StringComparison.Ordinal) ? "text/csv"
            : "text/plain";

        var existing = destList.FindIndex(b => string.Equals(b.Name, options.DestinationBlobName, StringComparison.OrdinalIgnoreCase));
        var newItem = new StorageBlobItem(options.DestinationBlobName, false, bytes, contentType, DateTimeOffset.UtcNow, "\"demo-etag-copy\"");
        if (existing >= 0)
        {
            destList[existing] = newItem;
        }
        else
        {
            destList.Add(newItem);
        }

        return Task.FromResult(new BlobMutationResult(true, ResultBlobPath: destKey));
    }

    public Task<BlobMutationResult> SetBlobMetadataAsync(
        string containerName, string blobName, IDictionary<string, string> metadata,
        string? ifMatchEtag = null, CancellationToken ct = default)
    {
        var key = $"{containerName}/{blobName}";

        // Ensure a backing blob record exists; if it doesn't, the metadata update is a no-op failure.
        var allBlobs = _blobsByContainer.Values.SelectMany(b => b).Where(b => !b.IsPrefix).ToList();
        var exists = allBlobs.Any(b => string.Equals($"{containerName}/{b.Name}", key, StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            return Task.FromResult(new BlobMutationResult(false, ErrorMessage: $"Blob not found: {key}"));
        }

        if (!_blobMetadata.TryGetValue(key, out var current))
        {
            current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _blobMetadata[key] = current;
        }

        current.Clear();
        foreach (var kvp in metadata)
        {
            current[kvp.Key] = kvp.Value;
        }

        return Task.FromResult(new BlobMutationResult(true, ResultBlobPath: key));
    }

    public Task<BlobVersionComparison> GetVersionComparisonAsync(
        string containerName, string blobName, string baseVersionId,
        string? compareVersionId = null, CancellationToken ct = default)
    {
        var diff = new BlobMetadataDiff(
            Before: new Dictionary<string, string?> { ["author"] = "demo", ["version"] = "1" },
            After: new Dictionary<string, string?> { ["author"] = "demo", ["version"] = "2" },
            AddedKeys: [],
            RemovedKeys: [],
            ChangedKeys: ["version"]);

        return Task.FromResult(new BlobVersionComparison(
            BaseVersionId: baseVersionId,
            CompareVersionId: compareVersionId,
            MetadataDiff: diff,
            ContentComparePossible: true,
            BaseSizeBytes: 512,
            CompareSizeBytes: 540,
            TextDiff: "- version: 1\n+ version: 2"));
    }

    public Task<BlobRecoveryResult> RestoreBlobVersionAsync(
        string containerName, string blobName, string versionId, CancellationToken ct = default) =>
        Task.FromResult(new BlobRecoveryResult(
            BlobRecoveryState.Restored,
            ResultBlobPath: $"{containerName}/{blobName}"));

    public Task<BlobRecoveryResult> UndeleteBlobAsync(
        string containerName, string blobName, CancellationToken ct = default)
    {
        var deletedList = _deletedBlobsByContainer.GetValueOrDefault(containerName);
        if (deletedList is null)
        {
            return Task.FromResult(new BlobRecoveryResult(BlobRecoveryState.Unsupported, ErrorMessage: "Soft delete is not enabled or the blob was not found."));
        }

        var index = deletedList.FindIndex(b => string.Equals(b.Name, blobName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return Task.FromResult(new BlobRecoveryResult(BlobRecoveryState.Unsupported, ErrorMessage: $"Deleted blob not found: {blobName}"));
        }

        var deleted = deletedList[index];
        deletedList.RemoveAt(index);

        var key = $"{containerName}/{blobName}";
        var content = _blobContents.GetValueOrDefault(key) ?? "";
        var bytes = Encoding.UTF8.GetByteCount(content);
        var contentType = blobName.EndsWith(".json", StringComparison.Ordinal) ? "application/json"
            : blobName.EndsWith(".csv", StringComparison.Ordinal) ? "text/csv"
            : "text/plain";

        var list = _blobsByContainer.GetValueOrDefault(containerName);
        if (list is null)
        {
            list = [];
            _blobsByContainer[containerName] = list;
        }

        var existing = list.FindIndex(b => string.Equals(b.Name, blobName, StringComparison.OrdinalIgnoreCase));
        var newItem = new StorageBlobItem(blobName, false, bytes, contentType, DateTimeOffset.UtcNow, "\"demo-etag-restored\"");
        if (existing >= 0)
        {
            list[existing] = newItem;
        }
        else
        {
            list.Add(newItem);
        }

        return Task.FromResult(new BlobRecoveryResult(
            BlobRecoveryState.Undeleted,
            ResultBlobPath: key));
    }

    public Task<IReadOnlyList<DeletedBlobItem>> ListDeletedBlobsAsync(string containerName, string? prefix = null, CancellationToken ct = default)
    {
        var list = _deletedBlobsByContainer.GetValueOrDefault(containerName) ?? [];
        if (string.IsNullOrEmpty(prefix))
        {
            return Task.FromResult<IReadOnlyList<DeletedBlobItem>>(list);
        }

        return Task.FromResult<IReadOnlyList<DeletedBlobItem>>(
            list.Where(b => b.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList());
    }

    private static async Task WriteAsync(
        Stream destination,
        byte[] bytes,
        IProgress<long>? progress,
        CancellationToken ct)
    {
        const int chunkSize = 8 * 1024;
        var written = 0;

        while (written < bytes.Length)
        {
            var count = Math.Min(chunkSize, bytes.Length - written);
            await destination.WriteAsync(bytes.AsMemory(written, count), ct).ConfigureAwait(false);
            written += count;
            progress?.Report(written);
        }
    }
}
