# Status — Storage Account Viewer

---

title: "Status - Storage Account Viewer"
owner: ""
state: "Proposed"
branch: ""
started: ""
last_updated: "2026-03-18"

---

## Quick summary

Current state: **Proposed** — feature plan created, no implementation started.

**Current focus:** Plan documents complete. Awaiting design review before any code work begins.

## Progress checklist

- [x] Planning complete
- [ ] Design reviewed
- [ ] `Azure.Storage.Blobs` package added to `SwebKit.Azure.csproj`
- [ ] `StorageConfig` model created in `SwebKit.Core/Domain/`
- [ ] `ProjectEnvironment` extended with `StorageConfig? Storage`
- [ ] `IStorageClient` interface created in `SwebKit.Core/Abstractions/`
- [ ] Model types created (`StorageContainerItem`, `StorageBlobItem`, `StorageBlobPage`, `BlobProperties`, `StorageBlobContent`)
- [ ] `AzureStorageClient` implemented in `SwebKit.Azure/Storage/`
- [ ] `IStorageClient` / `AzureStorageClient` registered in DI (`MauiProgram.cs`)
- [ ] `StorageConfigForm.razor` implemented and wired into Settings page
- [ ] Nav tree entry for Storage added
- [ ] `_Imports.razor` updated with `@using SwebKit.App.Components.Storage`
- [ ] `StoragePage.razor` scaffolded (left pane + tabbed center)
- [ ] `StorageContainerTree.razor` implemented (on-demand load, loading state)
- [ ] `StorageBlobList.razor` implemented (grid + breadcrumb + virtual folder nav + pagination)
- [ ] `BlobDetailPane.razor` implemented (property table + preview + download + copy URL/SAS)
- [ ] Inline preview: JSON/XML pretty-print + copy button
- [ ] Preview size gate (512 KB warn / 2 MB hard cap)
- [ ] SAS URL generation with expiry
- [ ] Download to local filesystem via save-file dialog
- [ ] Copy direct URL to clipboard
- [ ] Unit tests passing (UT-1 through UT-10, UT-C1 through UT-C3)
- [ ] Component render tests passing (CT-1 through CT-4)
- [ ] Manual smoke test: real storage account via AAD
- [ ] Manual smoke test: real storage account via connection string
- [ ] Docs aligned (`architecture/functionalities/` updated)

## Completed

- Feature scope, backend, frontend, and test-plan documents drafted.

## Remaining

- All implementation tasks. See [backend.md](backend.md) and [frontend.md](frontend.md) for details and sequencing.

## Blockers

- None recorded.

## Validation

See [test-plan.md](test-plan.md).

- Automated: Not started
- Manual: Not started
