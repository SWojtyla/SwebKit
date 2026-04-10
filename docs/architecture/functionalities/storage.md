# Storage (Azure Blob)

## What Is Supported

- Per-environment `StorageConfig`: account name, connection string credential ref, and AAD flag.
- Browse all containers visible to the configured credential.
- Navigate virtual folder hierarchies using the `/` delimiter and `BlobHierarchyItem` prefix traversal.
- Breadcrumb path bar reflecting the current virtual folder depth.
- Blob list grid: name, human-readable size, content-type, last modified, action buttons.
- Full blob properties panel: metadata, tags, ETag, content-type, lease status, access tier, size.
- Inline content preview for text, JSON, and XML blobs.
  - JSON pretty-printed via `System.Text.Json`; fallback to raw text on malformed input.
  - Size-gated: warn at 512 KB; hard cap at 2 MB with "Load anyway" escape.
- Download blobs and blob versions to the user's Downloads folder with inline in-flight progress in the blob list and detail pane.
- Copy blob direct URL to clipboard (no SAS expiry).
- Copy SAS URL with 24-hour expiry generated client-side via the SDK.
- Storage config form in Settings page (Account Name, Use AAD, Connection String Ref, Test Connection).

## Core Runtime Flow

1. `StoragePage` reads `AppState.CurrentEnvironment?.Storage`.
2. If null: shows "not configured" prompt with link to Settings.
3. If set: constructs `AzureStorageClient(config, CredentialStore)` directly (no DI; same pattern as Redis/AKS).
4. `StorageContainerTree` calls `ListContainersAsync` on first render; selection fires `SelectedContainerChanged`.
5. `StorageBlobList` calls `ListBlobsAsync` with current prefix and pagination token; breadcrumb segments drive prefix navigation.
6. Selecting a blob row renders `BlobDetailPane`, which calls `GetBlobPropertiesAsync` and `GetBlobContentAsync` concurrently.
7. Single-file downloads in `StorageBlobList` and `BlobDetailPane` pass a byte-progress callback through `IStorageClient.DownloadBlobAsync`; the UI renders determinate progress when blob size is known and falls back to an indeterminate in-flight state otherwise.
8. SAS URL generation via `GetBlobSasUrlAsync`; failures surfaced inline (not dialog) per UX decision.

## Credential Modes

| Mode                                 | Config                         | SDK client                                                  |
| ------------------------------------ | ------------------------------ | ----------------------------------------------------------- |
| AAD (`UseAad = true`)                | `AccountName` required         | `BlobServiceClient(Uri, DefaultAzureCredential)`            |
| Connection string (`UseAad = false`) | `ConnectionStringRef` required | `BlobServiceClient(connectionString from ICredentialStore)` |

SAS URL generation requires shared key access (`allowSharedKeyAccess = true`). If disallowed, `RequestFailedException` is caught and surfaced with an actionable inline message.

## Main Code Locations

- `src/SwebKit.App/Components/Pages/StoragePage.razor`
- `src/SwebKit.App/Components/Pages/StorageConfigForm.razor`
- `src/SwebKit.App/Components/Storage/StorageContainerTree.razor`
- `src/SwebKit.App/Components/Storage/StorageBlobList.razor`
- `src/SwebKit.App/Components/Storage/BlobDetailPane.razor`
- `src/SwebKit.Core/Abstractions/IStorageClient.cs`
- `src/SwebKit.Core/Domain/StorageConfig.cs`
- `src/SwebKit.Core/Domain/StorageModels.cs`
- `src/SwebKit.Azure/Storage/AzureStorageClient.cs`

## Important Notes

- Read-only in MVP. Write operations (upload, delete) are out of scope and require an explicit per-environment mutations toggle when added.
- Pagination: `ListBlobsAsync` returns one page (default 100 items) with a continuation token. "Load more" appends the next page in the UI.
- Binary detection: `GetBlobContentAsync` checks content-type before issuing a byte-range read. Binary blobs return `IsBinary = true` without downloading content.
- Tags require a separate `GetTagsAsync` call (not included in `GetPropertiesAsync`). Both calls are made concurrently via `Task.WhenAll`.
- Download progress is local to the initiating surface; there is no background transfer manager or cross-page download queue.

## Validation Pointers

- `tests/SwebKit.Azure.Tests/AzureStorageClientTests.cs` — constructor-guard tests
- `tests/SwebKit.Core.Tests/StorageConfigTests.cs` — JSON serialization round-trip tests
