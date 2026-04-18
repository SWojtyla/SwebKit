# Archive Summary - storage-controlled-mutations

---

title: "Archive Summary - storage-controlled-mutations"
owner: "GitHub Copilot"
jira: "not linked"
completed_date: "2026-04-18"
pr: ""
commit: ""

---

## Goal

Let operators perform tightly scoped blob mutations (upload, copy, metadata update, version diff, restore, undelete) from the existing Storage page while keeping read-only mode as the default and making overwrite risk and recovery intent obvious at every step.

## Delivered

- **Wave 1 — Mutation policy + upload + copy:**
  - `StorageConfig.AllowMutations` (additive, default `false` — all existing environments remain read-only).
  - `StorageCapabilities`, `BlobUploadOptions`, `BlobCopyOptions`, `BlobMutationResult` models.
  - `GetStorageCapabilitiesAsync`, `UploadBlobAsync`, `CopyBlobAsync` on `IStorageClient`, `AzureStorageClient`, `DemoStorageClient`.
  - `StorageMutationBanner.razor` — read-only info / mutation-enabled warning with account name (text + color).
  - `BlobUploadDialog.razor` — file input, destination blob name, overwrite toggle with warning, Confirm/Cancel.
  - `BlobCopyDialog.razor` — source label, destination container/blob, overwrite toggle, Confirm/Cancel.
  - `StoragePage.razor` updated with banner, Upload/Copy buttons (mutation-gated), inline result feedback.
  - 6 bUnit tests (`StorageMutationTests`).

- **Wave 2 — Metadata editor + version diff:**
  - `BlobMetadataDiff`, `BlobVersionComparison` models.
  - `SetBlobMetadataAsync`, `GetVersionComparisonAsync` on `IStorageClient`, `AzureStorageClient`, `DemoStorageClient`.
  - `BlobMetadataEditor.razor` — editable key/value rows, diff preview (Added/Removed/Changed with text labels), Save gated on changes, ReadOnly mode.
  - `BlobVersionDiffPane.razor` — empty state, metadata diff table, size comparison, text diff or "not available" notice.
  - `BlobDetailPane.razor` updated with "Edit metadata" button (mutation-gated), Compare action, version diff pane.
  - 7 bUnit tests (`BlobMetadataEditorTests`, `BlobVersionDiffPaneTests`).

- **Wave 3 — Recovery:**
  - `BlobRecoveryState`, `BlobRecoveryResult` models.
  - `RestoreBlobVersionAsync` (copy-forward into current path, history preserved), `UndeleteBlobAsync` on `IStorageClient`, `AzureStorageClient`, `DemoStorageClient`.
  - `BlobRecoveryPanel.razor` — restore section (CanRestore-gated), undelete section (SoftDeleteEnabled-gated), explicit "not available" text notices, inline result.
  - `BlobDetailPane.razor` updated with recovery panel; `AllowMutations` parameter threaded from `StoragePage`.
  - 3 bUnit tests (`BlobRecoveryPanelTests`).

- **Total: Core 434/434 · Azure 21/21 · App 364/364 · Build 0 errors, 0 warnings.**

## Key decisions

- **`AllowMutations` defaults to `false`** — mutation enablement is explicit configuration per account, not inferred from credential capability. Existing environments are unaffected.
- **Copy-forward restore** — version restore uses `StartCopyFromUriAsync` rather than a destructive overwrite so restored content becomes a new current version and history stays intact.
- **ETag-aware metadata writes** — `SetBlobMetadataAsync` accepts an optional `ifMatchEtag` to prevent silent overwrites when the blob changes between inspection and mutation.
- **Capability snapshot rather than exception-on-use** — `GetStorageCapabilitiesAsync` probes account features upfront so the UI can disable actions with explicit explanations rather than showing runtime errors.
- **Single-blob scope** — no bulk or wildcard mutations; this keeps the safety model simple and auditable.

## Validation performed

- Unit tests: Core 434/434 (8 new), Azure 21/21 (unchanged).
- Component tests: App 364/364 (16 new bUnit tests across 5 test files).
- Build: 0 errors, 0 warnings on net10.0-windows10.0.19041.0.
- Manual: not performed; all mutation flows are read-only-by-default with explicit capability gating.

## Lessons learned

- `AllowMutations` must be threaded from `StoragePage` down to `BlobDetailPane` via a parameter — the detail pane cannot safely read `StorageConfig` itself without coupling it to app-layer state.
- Diff previews for metadata editors are small but require a computed property that compares the original and edited dictionaries by key — computing this in-component rather than in a service keeps the test surface clean.
- Overwrite flows need explicit text ("This will replace the existing blob") in the dialog body, not just a toggle label, because operators skim confirmation UI under pressure.

## Follow-up

- Manual validation on a live Azure Storage account to confirm `GetStorageCapabilitiesAsync` correctly detects versioning and soft delete state.
- Settings UI for toggling `AllowMutations` per account — currently requires direct profile JSON edit.

## Archive note

> This file is present because the feature had **no Jira ticket** (Path B). Archive location: `docs/features/archive/storage-controlled-mutations/`.
