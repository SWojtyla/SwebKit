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
- Mutation-gated upload, in-account blob copy, and metadata edit workflows when the selected storage profile allows mutations.
- Download blobs and blob versions to the user's Downloads folder with inline in-flight progress in the blob list and detail pane.
- Download the currently loaded blob selection in the active folder as a ZIP archive in the user's Downloads folder.
- Browse blob version history, compare a historical version against the current blob, and restore a selected version of the blob when the storage profile allows mutations.
- Container and blob filtering inside the active account workspace.
- Copy the selected blob path and the currently loaded text preview to the clipboard.
- Copy the active container SAS URL to the clipboard.
- Copy blob direct URL to clipboard (no SAS expiry).
- Copy SAS URL with 24-hour expiry generated client-side via the SDK.
- Shared shell workspace snapshots for the selected account, container, and blob so recent/favorite items and named favorites can reopen Storage context.
- Storage config form in Settings page (Account Name, Use AAD, Connection String Ref, Test Connection).

## Core Runtime Flow

1. `StoragePage` reads `AppState.CurrentEnvironment?.Storage`.
2. If null: shows "not configured" prompt with link to Settings.
3. If set: the hosted page or the WinUI viewmodel resolves either the demo storage client or a factory-created live `IStorageClient` for the selected account.
4. `StorageContainerTree` or the native container list calls `ListContainersAsync` on first render; selection changes the active container context.
5. `StorageBlobList` or the native blob workspace calls `ListBlobsAsync` with the current prefix and pagination token; breadcrumb segments drive prefix navigation.
6. Selecting a blob row renders `BlobDetailPane`, which calls `GetBlobPropertiesAsync` and `GetBlobContentAsync` concurrently.
7. The native detail pane also loads storage capabilities and blob versions; because the data-plane SDK cannot prove account-level versioning, version-history workflows stay enabled and the per-blob list determines whether restore should be offered.
8. Single-file downloads in `StorageBlobList` and `BlobDetailPane` pass a byte-progress callback through `IStorageClient.DownloadBlobAsync`; the UI renders determinate progress when blob size is known and falls back to an indeterminate in-flight state otherwise.
9. Bulk ZIP download streams the currently loaded selection into a ZIP archive in Downloads; selection stays local to the active folder view.
10. Mutation-enabled workflows call `UploadBlobAsync`, `CopyBlobAsync`, and `SetBlobMetadataAsync`; the UI keeps those actions gated behind the selected profile's `AllowMutations` flag.
11. SAS URL generation via `GetBlobSasUrlAsync` and `GetContainerSasUrlAsync`; failures surfaced inline or through the native status surfaces.
12. Account, container, and blob selection changes publish a semantic workspace snapshot; route-first restore reapplies that selection through `StoragePage`.

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
- `src/SwebKit.WinUI/Views/Storage/StoragePage.xaml`
- `src/SwebKit.WinUI/ViewModels/Storage/StoragePageViewModel.cs`
- `src/SwebKit.Core/Abstractions/IStorageClient.cs`
- `src/SwebKit.Core/Domain/StorageConfig.cs`
- `src/SwebKit.Core/Domain/StorageModels.cs`
- `src/SwebKit.Azure/Storage/AzureStorageClient.cs`

## Important Notes

- Storage remains browse-first, but mutation-enabled profiles can now upload blobs, copy blobs within the same account, edit metadata, and restore historical versions. Version restore still follows the available version list and the per-profile `AllowMutations` toggle because the current data-plane SDK cannot prove account-level versioning up front.
- Pagination: `ListBlobsAsync` returns one page (default 100 items) with a continuation token. "Load more" appends the next page in the UI.
- ZIP download works over the blobs currently loaded in the active folder view. Operators must page in additional blobs before selecting them for the archive.
- Deleted-blob recovery is not currently first-class in either surface because the workspace list does not yet enumerate soft-deleted blobs.
- Binary detection: `GetBlobContentAsync` checks content-type before issuing a byte-range read. Binary blobs return `IsBinary = true` without downloading content.
- Tags require a separate `GetTagsAsync` call (not included in `GetPropertiesAsync`). The current client fetches properties first and then fetches tags from the same blob client.
- Download progress is local to the initiating surface; there is no background transfer manager or cross-page download queue.
- Workspace restore is semantic and lightweight. Storage reopens the selected account/container/blob context rather than trying to preserve a live client object.
- The native WinUI Storage route now keeps account selection, connection summary, and route actions in the shared compact scaffold context band so the container and blob workspace starts earlier on desktop.

## Validation Pointers

- `tests/SwebKit.Azure.Tests/AzureStorageClientTests.cs` — constructor-guard tests
- `tests/SwebKit.Core.Tests/StorageConfigTests.cs` — JSON serialization round-trip tests
- `tests/SwebKit.WinUI.Tests/StoragePageViewModelTests.cs` — WinUI version, ZIP download, upload, copy, and metadata-edit page-state coverage
