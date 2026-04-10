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
- The toolbar exposes `Select all loaded`, and Redis tree row clicks drive multi-select directly: leaf rows toggle one key, namespace rows toggle their loaded descendants, and the chevron remains the dedicated expand/collapse control.
- The pattern helper now states that filtering is applied across the full Redis keyspace while the tree is limited to the currently loaded matches.
- The initial Redis loaded-match page stays intentionally bounded, oversized SCAN batches are buffered for the next `Load more matches` action, and key-type badges are filled in with lightweight batched lookups so the tree can render before all badge metadata arrives.
- New scan, filter, or cache contexts supersede older badge batches so stale type writes do not leak into the next tree state.
- Selection stays reversible and the selected-key count remains visible while multi-select mode is active.
- Selected key rows use a stronger border, accent rail, and weight treatment so the current selection is obvious at a glance, and namespace rows surface partial or full loaded-subtree selection with the same visual language.

## API / contract changes

- `IStorageClient` now exposes additive progress reporting so the UI can receive byte updates without polling.
- `IRedisClient.DeleteKeysAsync` remains the only destructive primitive used by the page.
- No new prefix-delete or wildcard-delete APIs were added for Redis.
- `IRedisClient.FlushDatabaseAsync` remains out of the main Redis page UX.
- `IRedisClient` now exposes a lightweight `GetKeyTypeAsync` call so the tree can resolve badges without forcing full key metadata reads for every loaded match.

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
- [x] Routed Redis multi-select through direct row clicks so a key toggles itself and a namespace toggles its loaded descendants without separate subtree badges.
- [x] Reused `_toolbar.SelectedKeys` plus `DeleteSelectedKeysAsync` chunked deletion instead of adding a new delete path.
- [x] Kept selected counts explicit when scan pagination means not all keys are loaded.
- [x] Bounded the initial Redis loaded-match page and kept `Load more` on the same filtered cursor to prevent large key scans from freezing the tree.
- [x] Strictly capped each loaded match page even when Redis returns an oversized SCAN batch, carrying overflow into the next `Load more` action instead of dropping it or rendering it early.
- [x] Switched tree badge loading to lightweight batched key-type calls so scan completion no longer waits on full key metadata fan-out.
- [x] Tied batched badge writes to the current scan/filter/cache context so stale work is canceled or ignored before it mutates the next tree state.
- [x] Strengthened the selected-row visual treatment in the namespace tree.

## Validation

- Targeted automated validation passed in `RedisToolbarTests.cs`, `RedisNamespaceTreeNodeTests.cs`, `DemoRedisClientTests.cs`, and `RedisScanPageAccumulatorTests.cs` (28/28).
- `dotnet build .\SwebKit.slnx -nologo` succeeded.
- Manual UI spot-checks remain optional before ship if a final visual pass is requested.

## Notes

- Storage progress updates should avoid high-frequency `StateHasChanged` churn; coalesce updates if needed.
- Redis row-driven selection must operate on the loaded tree only; hidden unscanned keys must never be silently included.
- Redis scan copy must keep the distinction explicit: the filter is keyspace-wide, but the tree and bulk helpers operate on the currently loaded matches.
- Redis scan-session boundaries must stay authoritative so older badge batches cannot repopulate the next cache or filter state.
