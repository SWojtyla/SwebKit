# Backend Plan — Storage Account Viewer

---

title: "Backend Plan - Storage Account Viewer"
owner: ""
status: "Not started"
created: "2026-03-18"
updated: "2026-03-18"

---

## Goal

Add a read-only Azure Blob Storage client behind a clean `IStorageClient` abstraction that
follows the same credential-resolution, config-model, and error-surfacing patterns
used by `AzureServiceBusClient`. The implementation must support both connection-string
mode (secret resolved via `ICredentialStore`) and AAD mode (`DefaultAzureCredential`),
and must handle paginated listing of containers and blobs, virtual folder prefix
navigation, safe inline content retrieval, and SAS URL generation.

---

## Impacted areas

| File                                              | Change                                      |
| ------------------------------------------------- | ------------------------------------------- |
| `src/SwebKit.Core/Domain/StorageConfig.cs`        | **New** config model                        |
| `src/SwebKit.Core/Domain/ProjectEnvironment.cs`   | Add `StorageConfig? Storage` property       |
| `src/SwebKit.Core/Abstractions/IStorageClient.cs` | **New** interface                           |
| `src/SwebKit.Core/Domain/StorageModels.cs`        | **New** model record types                  |
| `src/SwebKit.Azure/Storage/AzureStorageClient.cs` | **New** implementation                      |
| `src/SwebKit.Azure/SwebKit.Azure.csproj`          | Add `Azure.Storage.Blobs` package reference |
| `src/SwebKit.App/MauiProgram.cs`                  | DI registration                             |

---

## Design

### 1 — Config model: `StorageConfig`

```csharp
// src/SwebKit.Core/Domain/StorageConfig.cs
public sealed class StorageConfig
{
    /// <summary>
    /// Storage account name. Required when UseAad = true.
    /// Used to build the blob service endpoint: https://{AccountName}.blob.core.windows.net
    /// </summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>
    /// Key in ICredentialStore that maps to the full connection string.
    /// Required when UseAad = false. Ignored when UseAad = true.
    /// </summary>
    public string? ConnectionStringRef { get; set; }

    /// <summary>
    /// When true, authenticate with DefaultAzureCredential using AccountName.
    /// When false, authenticate with the connection string from ConnectionStringRef.
    /// </summary>
    public bool UseAad { get; set; }
}
```

Add to `ProjectEnvironment`:

```csharp
public StorageConfig? Storage { get; set; }
```

`StorageConfig` is nullable to match the existing pattern for optional per-environment
service configs (Redis, Service Bus). If `Storage` is null, the Storage nav entry is
hidden/greyed out.

---

### 2 — `IStorageClient` interface

```csharp
// src/SwebKit.Core/Abstractions/IStorageClient.cs
public interface IStorageClient
{
    StorageConfig Config { get; }

    /// <summary>
    /// Verifies connectivity by attempting the listing operation used for real work.
    /// Returns true on success; throws a descriptive exception on failure so the
    /// caller can surface the error message in the UI.
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns all containers visible to the configured credential.
    /// </summary>
    Task<IReadOnlyList<StorageContainerItem>> ListContainersAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns one page of blobs and virtual-folder prefixes inside a container.
    /// prefix = "" for the root; "images/" for a virtual folder named "images".
    /// Uses the "/" delimiter for hierarchical (non-flat) listing.
    /// Caller advances pages by passing the returned ContinuationToken.
    /// </summary>
    Task<StorageBlobPage> ListBlobsAsync(
        string containerName,
        string prefix,
        string? continuationToken = null,
        int pageSize = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Returns full properties for a single named blob (metadata, tags, tiers, lease, etc.).
    /// </summary>
    Task<BlobProperties> GetBlobPropertiesAsync(
        string containerName,
        string blobName,
        CancellationToken ct = default);

    /// <summary>
    /// Reads blob content as UTF-8 text, up to maxBytes.
    /// Returns WasTruncated = true if the blob is larger than maxBytes.
    /// Returns IsBinary = true if content-type is not text/*, application/json,
    /// application/xml, or application/x-www-form-urlencoded.
    /// For binary blobs, Content is empty and IsBinary is set so the UI can show
    /// a "binary content — use download" notice without attempting read.
    /// </summary>
    Task<StorageBlobContent> GetBlobContentAsync(
        string containerName,
        string blobName,
        int maxBytes = 524_288,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a service SAS URI for a blob with the specified validity window.
    /// Requires shared key access on the storage account.
    /// Throws RequestFailedException (AuthorizationPermissionMismatch) if the account
    /// disallows shared key access — callers should catch and surface a clear message.
    /// </summary>
    Task<string> GetBlobSasUrlAsync(
        string containerName,
        string blobName,
        TimeSpan expiry,
        CancellationToken ct = default);

    /// <summary>
    /// Streams blob content to the supplied destination stream.
    /// Suitable for download-to-disk operations; no size cap applied.
    /// </summary>
    Task DownloadBlobAsync(
        string containerName,
        string blobName,
        Stream destination,
        CancellationToken ct = default);
}
```

---

### 3 — Model types

```csharp
// src/SwebKit.Core/Domain/StorageModels.cs

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
```

---

### 4 — `AzureStorageClient` implementation

**File:** `src/SwebKit.Azure/Storage/AzureStorageClient.cs`

#### Credential resolution

Mirror the Service Bus client pattern: `ICredentialStore.GetSecret(ConnectionStringRef)`
for connection-string mode; `DefaultAzureCredential` + account endpoint URL for AAD mode.
Both paths construct a `BlobServiceClient` once in the constructor.

```
if UseAad = false AND ConnectionStringRef is set
    → connectionString = credentialStore.GetSecret(ConnectionStringRef)
    → new BlobServiceClient(connectionString)

if UseAad = true AND AccountName is set
    → uri = new Uri($"https://{AccountName}.blob.core.windows.net")
    → new BlobServiceClient(uri, new DefaultAzureCredential())

otherwise → throw InvalidOperationException with config guidance
```

#### TestConnectionAsync

Use the same listing operation as `ListContainersAsync` — advance one step through
the pageable and break. This validates real permissions (pattern mirrors pitfall AZ-1:
the test method must exercise the same code path as the real operation).

```csharp
await foreach (var _ in _blobService.GetBlobContainersAsync(cancellationToken: ct))
    break;
```

#### ListContainersAsync

Enumerate all pages of `GetBlobContainersAsync` and map to `StorageContainerItem`.
`PublicAccess` is mapped from `BlobContainerProperties.PublicAccess.ToString()`.
Use `await foreach` + `await using` to satisfy pitfall AZ-3.

#### ListBlobsAsync — virtual folder navigation

Use `GetBlobsByHierarchyAsync(delimiter: "/", prefix: prefix)` to get a mix
of `BlobHierarchyItem` results. Items where `IsBlob = true` are real blobs;
items where `IsPrefix = true` are virtual folder prefixes (navigable one level deeper).

Map to `StorageBlobItem`:

- `IsPrefix = true` → `StorageBlobItem(Name: item.Prefix, IsPrefix: true, …nulls)`
- `IsBlob = true` → map `item.Blob` properties normally

Pagination: use `AsyncPageable<BlobHierarchyItem>.AsPages(continuationToken, pageSize)`
and take the **first returned page** only for each call. Return the page's `ContinuationToken`.

```csharp
await foreach (var page in _container
    .GetBlobsByHierarchyAsync(delimiter: "/", prefix: prefix)
    .AsPages(continuationToken, pageSize))
{
    // process page.Values, return page.ContinuationToken
    break;
}
```

#### GetBlobPropertiesAsync

```csharp
var blobClient = _blobService.GetBlobContainerClient(containerName).GetBlobClient(blobName);
var response = await blobClient.GetPropertiesAsync(cancellationToken: ct);
// map Azure.Storage.Blobs.Models.BlobProperties → SwebKit BlobProperties record
```

Tags require a separate `GetTagsAsync` call (tags are not included in `GetPropertiesAsync`
response). Make both calls concurrently with `Task.WhenAll`.

#### GetBlobContentAsync

1. Call `GetBlobPropertiesAsync` first to check `ContentType` and `SizeBytes`.
2. If `ContentType` indicates binary (not `text/*`, `application/json`,
   `application/xml`, `application/x-www-form-urlencoded`), return early with
   `IsBinary = true`, `Content = string.Empty`, `WasTruncated = false`.
3. If `SizeBytes > maxBytes`, note `WasTruncated = true` but still read `maxBytes`
   bytes using `DownloadContentAsync` with a bounded range
   (`new HttpRange(0, maxBytes)`).
4. Decode bytes as UTF-8; return `StorageBlobContent`.

#### GetBlobSasUrlAsync

Use `BlobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(expiry))`.
This requires the `BlobServiceClient` to have been created with a connection string or
storage key. If the account uses AAD-only (`allowSharedKeyAccess = false`), the SDK throws;
catch `RequestFailedException` and propagate with a descriptive message.

#### DownloadBlobAsync

```csharp
var blobClient = _blobService.GetBlobContainerClient(containerName).GetBlobClient(blobName);
await blobClient.DownloadToAsync(destination, cancellationToken: ct);
```

No size cap; the caller is responsible for providing a file stream. Progress is not
tracked in the MVP.

---

### 5 — DI registration

In `MauiProgram.cs`, register `IStorageClient` using the same factory pattern as
existing services: construct `AzureStorageClient` from the active environment's
`StorageConfig` whenever the environment changes. If `StorageConfig` is null, do not
register (or register a no-op sentinel that throws `NotConfiguredException`).

The exact pattern should mirror how `IServiceBusClient` or `IRedisClient` are rebuilt on
environment change. Confirm the pattern by reading `MauiProgram.cs` and the
`AppStateService` environment-change logic before implementing.

---

## Package reference

Add to `src/SwebKit.Azure/SwebKit.Azure.csproj`:

```xml
<PackageReference Include="Azure.Storage.Blobs" Version="12.*" />
```

Pin to the latest `12.x` stable release. Do not add `Azure.Data.Tables` or
`Azure.Storage.Queues` until Table/Queue Storage scope is committed.

---

## Implementation tasks

- [ ] 1. Add `Azure.Storage.Blobs` package to `SwebKit.Azure.csproj`
- [ ] 2. Create `src/SwebKit.Core/Domain/StorageConfig.cs`
- [ ] 3. Create `src/SwebKit.Core/Domain/StorageModels.cs` with all record types
- [ ] 4. Add `StorageConfig? Storage` to `ProjectEnvironment`
- [ ] 5. Create `src/SwebKit.Core/Abstractions/IStorageClient.cs`
- [ ] 6. Create `src/SwebKit.Azure/Storage/AzureStorageClient.cs` — `BuildBlobServiceClient` + constructor
- [ ] 7. Implement `TestConnectionAsync`
- [ ] 8. Implement `ListContainersAsync`
- [ ] 9. Implement `ListBlobsAsync` (hierarchical with delimiter + pagination)
- [ ] 10. Implement `GetBlobPropertiesAsync` (properties + tags concurrently via `Task.WhenAll`)
- [ ] 11. Implement `GetBlobContentAsync` (binary detection + size gate + range read)
- [ ] 12. Implement `GetBlobSasUrlAsync` (catch and wrap `RequestFailedException`)
- [ ] 13. Implement `DownloadBlobAsync`
- [ ] 14. Register `IStorageClient` in `MauiProgram.cs` — confirm factory pattern against existing clients first
- [ ] 15. Write unit tests (see `test-plan.md` UT-1 through UT-10, UT-C1 through UT-C3)
- [ ] 16. Verify CS-2 compliance: all `catch (Exception)` blocks re-throw `OperationCanceledException`

## Validation

- Unit tests: Not started
- Manual checks: connect to a real storage account (AAD and connection string paths); listing, virtual folder nav, preview, download, SAS URL
