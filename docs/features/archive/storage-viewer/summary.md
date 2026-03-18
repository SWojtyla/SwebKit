# Archive Summary — Storage Account Viewer

---

title: "Archive Summary - Storage Account Viewer"
owner: ""
completed_date: "2026-03-18"
pr: ""
commit: ""

---

## Goal

Give .NET developers a fast, readable Azure Blob Storage browser embedded in SwebKit —
inspect containers, navigate virtual folder hierarchies, view blob properties and inline
content, and download or copy blob/SAS URLs without leaving the tool.

## Delivered

- `StorageConfig` model with `Id`, `DisplayName`, `AccountName`, `ConnectionStringRef`, and `UseAad` flag; multi-account list on `ProjectEnvironment`.
- `IStorageClient` abstraction in `SwebKit.Core/Abstractions/` with `ListContainersAsync`, `ListBlobsAsync`, `GetBlobPropertiesAsync`, `GetBlobContentAsync`, and SAS/download helpers.
- `AzureStorageClient` in `SwebKit.Azure/Storage/` supporting both connection-string and AAD (`DefaultAzureCredential`) modes.
- 403-tolerant `GetBlobPropertiesAsync`: tags fetch silently degrades when the AAD user lacks the Tag Reader role.
- `AzureStorageClient` constructed on-demand by `StoragePage` (no DI registration — consistent with Redis/AKS pattern).
- `StoragePage.razor`: multi-account dropdown selector (auto-selects when only one account configured).
- `StorageContainerTree.razor`: on-demand load with loading and error states.
- `StorageBlobList.razor`: breadcrumb nav, virtual folder traversal via prefix + delimiter, paginated blob grid, row-click folder navigation.
- `BlobDetailPane.razor`: full property panel (metadata, tags, ETag, content-type, lease, tier, size); inline preview for text/JSON/XML; size gate (512 KB warn / 2 MB hard cap with "Load anyway" escape); copy URL; SAS URL generation with configurable expiry; download to local filesystem.
- `StorageConfigForm.razor`: multi-account list manager (add/edit/remove per environment) wired into Settings page.
- Navigation tree entry for Storage (☁ icon, `Area="storage"`).
- Architecture deep-dive at `docs/architecture/functionalities/storage.md`.

## Key decisions

- **Multi-account per environment** — replaced the original single `StorageConfig? Storage` property on `ProjectEnvironment` with `List<StorageConfig> StorageAccounts` to support scenarios where a developer works with several accounts in the same environment.
- **On-demand client construction** — `AzureStorageClient` is newed up in `StoragePage` rather than registered in DI, matching the existing Redis and AKS pattern and keeping startup lean.
- **403-tolerant tags fetch** — AAD users missing the Storage Blob Data Reader + Tag Reader role cannot fetch tags; silently returning empty tags is preferable to surfacing an auth error for a secondary property.
- **Read-only MVP** — write operations (upload, delete, rename) intentionally deferred to protect production data; SAS generation and download are considered safe low-risk actions.

## Validation performed

- 7 unit tests passing: constructor guard tests (`UT-C1`, `UT-C2`, `UT-C3`) + `AzureStorageClient` behavior tests.
- Build: 0 errors, 0 warnings.
- Component render tests (CT-1 through CT-4) deferred — not in scope for initial delivery.
- Manual smoke tests against a real storage account not completed before archiving (tracked as follow-up below).

## Lessons learned

- Keep 403-tolerance explicit in the client: blob tag APIs return 403 (not 404) for missing role assignments; swallowing only that specific status code prevents cascading UI failures.
- On-demand client construction (no DI) is a simpler pattern for connections that are per-config instance rather than app-wide singletons.

## Follow-up

- Manual smoke test: AAD auth against a real storage account.
- Manual smoke test: connection string auth against a real storage account.
- Component render tests CT-1 through CT-4 (skipped for initial delivery).
- Table Storage and Queue Storage viewers (out of scope, future feature).
- Write operations — upload, delete, rename (intentionally deferred for read-only MVP).

## Archive metadata

- Active feature folder removed: `docs/features/active/storage-viewer/`
- Architecture doc: `docs/architecture/functionalities/storage.md`
- Related features: `service-bus`, `redis`, `aks` (same on-demand client pattern)
