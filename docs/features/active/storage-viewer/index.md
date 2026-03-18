# Storage Account Viewer

---

title: "Storage Account Viewer"
owner: ""
status: "Proposed"
created: "2026-03-18"
updated: "2026-03-18"

---

## Goal

Give .NET developers a fast, readable Azure Blob Storage browser embedded in SwebKit —
so they can inspect containers, navigate virtual folder hierarchies, view blob properties
and inline content, and download or copy blob/SAS URLs without leaving the tool or fighting
the Azure portal UI.

## Value

The Azure portal's Storage browser is slow and obscures simple developer tasks: checking a
blob's metadata, previewing a JSON config file, verifying that a pipeline upload landed
correctly. Developers doing this repeatedly as part of debugging or data-verification
workflows lose time context-switching and waiting for portal page loads. SwebKit already
covers Service Bus, AKS, Redis, and Application Insights — Storage is a natural fourth
pillar that makes the tool the single go-to for Azure daily work.

## Scope

### In scope — MVP (Blob Storage)

- Per-environment `StorageConfig`: account name, connection string credential ref, and AAD flag.
- `IStorageClient` abstraction in `SwebKit.Core/Abstractions/`.
- `AzureStorageClient` implementation in `SwebKit.Azure/Storage/`.
- `StoragePage.razor`: left-pane container tree + tabbed center blob viewer.
- Virtual folder navigation using the `/` delimiter and `BlobHierarchyItem` with prefix traversal.
- Breadcrumb path bar reflecting the current virtual folder depth.
- Blob list grid: name, human-readable size, content-type, last modified, action buttons.
- `BlobDetailPane.razor`: full property panel (metadata, tags, ETag, content-type, lease
  status, access tier, size) plus inline content preview for text, JSON, and XML blobs.
- Inline preview is size-gated: warn at 512 KB; hard cap at 2 MB. "Load anyway" escape for
  the warning tier.
- Download blob to local filesystem via save dialog.
- Copy blob direct URL to clipboard (no SAS expiry; requires public container or network access).
- Copy SAS URL with configurable expiry (24 h default), generated client-side via the SDK.
- `StorageConfigForm.razor` wired into the Settings page.
- Navigation tree entry for Storage beside existing service entries.

### Out of scope — MVP

- Table Storage and Queue Storage (see Future scope).
- Write operations (upload, delete, rename) — read-only is intentional to protect production.
- Cross-subscription storage account discovery via Azure Resource Graph (manual config only).
- Binary or image file rendering beyond a "binary content — use download" notice.
- Lifecycle policy, CORS, or network rule management.
- Storage Analytics metrics charts.

### Future scope

- Table Storage: row browsing, filter and sort by partition/row key.
- Queue Storage: peek messages, dead-letter style inspection.
- Cross-subscription storage discovery (Resource Graph — pattern established by App Insights
  feature).
- Write operations (upload, delete) gated behind an explicit "Allow mutations" per-environment
  toggle.
- Binary preview (hex dump or image rendering for image content-types).
- Saved container/prefix bookmarks per environment.

## Dependencies

- `Azure.Storage.Blobs` NuGet package — **new**; add to `SwebKit.Azure.csproj`.
- `Azure.Identity` — already present; `DefaultAzureCredential` reused as-is.
- `ICredentialStore` — existing abstraction in `SwebKit.Core`; used to resolve the
  connection string from Windows Credential Manager by ref key.
- `AppStateService` — existing; used to get the active `ProjectEnvironment`.
- `ProjectEnvironment` model in `SwebKit.Core` — add `StorageConfig? Storage` field.
- `StorageConfigForm.razor` — new component routed through the Settings page navigation.
- `MauiProgram.cs` — DI registration of `IStorageClient` / `AzureStorageClient`.
- `_Imports.razor` — requires `@using SwebKit.App.Components.Storage` (see pitfall BL-1).

## Risks & mitigations

- **Risk:** Large blob preview causes UI freeze or excessive memory use.  
  **Mitigation:** `GetBlobContentAsync` enforces a hard 2 MB cap via a bounded `MemoryStream`; a
  512 KB soft warning is shown before the user triggers the read. The preview panel never holds
  more than one blob's content in memory at a time.

- **Risk:** SAS URL generation fails for AAD-only storage accounts that have disallowed shared
  key access (`allowSharedKeyAccess = false`).  
  **Mitigation:** Catch `RequestFailedException` with `AuthorizationPermissionMismatch` or
  `StorageErrorCode.AuthorizationPermissionMismatch` during SAS generation and surface: _"SAS URL
  generation requires shared key access. This account may have disallowed it. Use the direct URL
  instead."_

- **Risk:** Containers with millions of blobs make the initial listing slow.  
  **Mitigation:** `ListBlobsAsync` uses the SDK's `AsyncPageable` in page-by-page mode. First
  page of 100 items loads immediately; a "Load more" button fetches the next page via continuation
  token held in component state. See pitfall AZ-3 for enumerator disposal.

- **Risk:** Auth misconfiguration (UseAad=true + ConnectionStringRef set) is confusing.  
  **Mitigation:** `StorageConfigForm` validates immediately on blur: if `UseAad = true` and a
  connection string ref is filled, show an inline warning _"ConnectionStringRef is ignored when
  UseAad is enabled."_ Mirrors the Service Bus config pattern.

- **Risk:** Virtual folder prefix navigation produces unexpected results when blob names contain
  `/` intended as part of the file name, not a path separator.  
  **Mitigation:** No normalization is applied; items are displayed exactly as returned by the SDK
  (`BlobHierarchyItem.Blob.Name` / `BlobHierarchyItem.Prefix`). No renaming or rewriting.

- **Risk:** `DefaultAzureCredential` fails silently if no credential source is configured on the
  developer machine.  
  **Mitigation:** `TestConnectionAsync` is called on environment switch. On failure, surface a
  structured error message in the UI (pattern mirrors App Insights Log Viewer feature).

## Related documents

- [architecture.md](../../architecture/architecture.md)
- [design.md](../../architecture/design.md)
- [azure-sdk.md](../../pitfalls/azure-sdk.md) — AZ-3 (`AsyncPageable` enumerator disposal) applies directly to all listing calls.
- [blazor-maui.md](../../pitfalls/blazor-maui.md) — BL-1 (missing `@using`), BL-2 (`InvokeAsync(StateHasChanged)`), BL-3 (set guard before `await`).
- [dotnet-csharp.md](../../pitfalls/dotnet-csharp.md) — CS-2 (`OperationCanceledException` must not be swallowed).

## Quick links

- Status: [status.md](status.md)
- Backend: [backend.md](backend.md)
- Frontend: [frontend.md](frontend.md)
- Tests: [test-plan.md](test-plan.md)
