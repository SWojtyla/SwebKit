# Frontend Module - storage-redis-ux-enhancements

---

title: "Frontend Module - storage-redis-ux-enhancements"
owner: "GitHub Copilot"
status: "Review"

---

## Goal

Capture the delivered UX changes with minimal surface expansion: one shared progress-aware download pattern for storage, and one safer selection-driven bulk-delete flow for Redis.

## Impacted areas

- Existing paths likely to be touched:
- src/SwebKit.App/Components/Storage/StorageBlobList.razor
- src/SwebKit.App/Components/Storage/BlobDetailPane.razor
- src/SwebKit.App/Components/Pages/RedisPage.razor
- src/SwebKit.App/Components/Redis/RedisToolbar.razor
- src/SwebKit.App/Components/Redis/RedisNamespaceTree.razor
- src/SwebKit.App/Components/Redis/RedisNamespaceTreeNode.razor
- src/SwebKit.Core/Abstractions/IStorageClient.cs
- src/SwebKit.Azure/Storage/AzureStorageClient.cs

## UX notes

- Storage:
- The UI now uses one compact inline progress presentation near the existing action area instead of adding a global download manager.
- Determinate progress is shown when total size is already known from `StorageBlobItem`, `BlobProperties`, or `BlobVersionItem`.
- The download target remains the user's Downloads folder.
- Redis:
- The page-level `Purge All` affordance is removed from the main toolbar.
- Delete remains an explicit two-step flow: selection first, confirmation second.
- The toolbar exposes `Select all loaded`, and tree nodes expose `All` / `None` helpers for loaded descendant keys.
- Selection stays reversible and the selected-key count remains visible while multi-select mode is active.

## API / contract changes

- `IStorageClient` now exposes additive progress reporting so the UI can receive byte updates without polling.
- `IRedisClient.DeleteKeysAsync` remains the only destructive primitive used by the page.
- No new prefix-delete or wildcard-delete APIs were added for Redis.
- `IRedisClient.FlushDatabaseAsync` remains out of the main Redis page UX.

## Workstreams

### Workstream 1 - Storage download progress

Recommended implementing agent: UI-capable .NET agent.

- [x] Added additive progress reporting to `IStorageClient.DownloadBlobAsync`.
- [x] Updated `AzureStorageClient` to forward SDK byte progress instead of relying on a spinner-only UI.
- [x] Threaded progress state through `StorageBlobList` single-file download actions and `BlobDetailPane` download and version actions.
- [x] Prevented duplicate clicks while a given item is downloading and clear state on success or failure.
- [x] Kept ZIP bulk-download flow on its existing busy-indicator path.

### Workstream 2 - Redis safer bulk selection

Recommended implementing agent: UI-capable .NET agent.

- [x] Removed the toolbar binding for `OnPurgeAll` and the corresponding main-page destructive CTA.
- [x] Added a toolbar helper for `Select all loaded` and kept `Clear selection` visible while multi-select mode is active.
- [x] Added node-level callbacks so a namespace prefix can select or clear all loaded descendant keys.
- [x] Reused `_toolbar.SelectedKeys` plus `DeleteSelectedKeysAsync` chunked deletion instead of adding a new delete path.
- [x] Kept selected counts explicit when scan pagination means not all keys are loaded.

## Validation

- Targeted automated validation passed in `RedisToolbarTests.cs`, `RedisNamespaceTreeNodeTests.cs`, `StorageDownloadProgressTests.cs`, and `AzureStorageClientTests.cs` (16/16).
- `dotnet build .\SwebKit.slnx -nologo` succeeded.
- Manual UI spot-checks remain optional before ship if a final visual pass is requested.

## Notes

- Storage progress updates should avoid high-frequency `StateHasChanged` churn; coalesce updates if needed.
- Redis selection helpers must operate on the loaded tree only; hidden unscanned keys must never be silently included.
