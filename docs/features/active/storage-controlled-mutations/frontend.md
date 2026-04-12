# Frontend Plan - storage-controlled-mutations

---

title: "Frontend Plan - storage-controlled-mutations"
owner: "GitHub Copilot"
status: "Not started"

---

## Goal

Let operators perform tightly scoped blob mutations from the existing Storage page while making read-only mode, overwrite risk, and recovery intent obvious at every step.

## Impacted areas

- Existing pages and components:
- `src/SwebKit.App/Components/Pages/StoragePage.razor`
- `src/SwebKit.App/Components/Pages/StorageConfigForm.razor`
- `src/SwebKit.App/Components/Storage/StorageBlobList.razor`
- `src/SwebKit.App/Components/Storage/BlobDetailPane.razor`
- Likely new UI helpers or page-local components:
- `src/SwebKit.App/Components/Storage/StorageMutationBanner.razor`
- `src/SwebKit.App/Components/Storage/BlobUploadDialog.razor`
- `src/SwebKit.App/Components/Storage/BlobCopyDialog.razor`
- `src/SwebKit.App/Components/Storage/BlobMetadataEditor.razor`
- `src/SwebKit.App/Components/Storage/BlobVersionDiffPane.razor`
- `src/SwebKit.App/Components/Storage/BlobRecoveryPanel.razor`
- Likely impacted tests:
- `tests/SwebKit.App.Tests/StorageDownloadProgressTests.cs`
- Likely new focused tests such as `StorageMutationDialogTests.cs`, `BlobVersionDiffPaneTests.cs`, and `BlobRecoveryPanelTests.cs`.

## UX notes

- Read-only remains the default experience.
- When `AllowMutations` is false, the page should show a calm read-only banner or inline note and continue to emphasize inspect/download workflows.
- When `AllowMutations` is true, the page should show an explicit mutation-enabled banner that includes account and environment context so the operator knows they are no longer in read-only mode.

### User flows

- Upload:
- Operator selects a container or prefix, opens an upload dialog, chooses a local file, reviews destination path and overwrite behavior, and confirms.
- Copy:
- Operator selects the current blob or a historical version, chooses a destination container/path, reviews overwrite behavior, and starts a same-account copy.
- Metadata update:
- Operator opens the blob detail properties view, edits metadata in a focused editor, reviews a diff, and applies the patch.
- Version diff:
- Operator opens the versions tab, selects one or two versions for comparison, and reviews either a text diff or a metadata-only comparison.
- Recovery:
- Operator selects a version or a deleted blob recovery candidate, reviews the recovery target and consequence, then confirms the recovery action.

### Confirmation and production safety

- Any action that can overwrite existing content must show:
- account name
- container name
- destination blob path
- source version or blob if applicable
- whether a current blob will be replaced
- In production, overwrite, restore, and recovery flows should require typed `CONFIRM` using the existing confirmation pattern already used in SwebKit.
- Metadata edits should show a before/after diff preview even in non-production because the action is easy to underestimate.
- Validation copy must make clear that restoring a version creates a new current version where versioning is enabled; it does not erase history.

### Component states

- Loading: per-dialog or per-panel progress rather than whole-page blocking.
- Capability unavailable: action disabled with explicit reason when account features or permissions do not support it.
- In progress: reuse the current Storage download-style progress treatment where sensible.
- Completed: inline success summary with the resulting blob path or version outcome.
- Failed: scoped error message near the relevant mutation surface.

### Accessibility

- Mutation banners and confirmation dialogs need explicit text warnings, not color alone.
- Version compare and recovery controls should be keyboard reachable from the versions tab.
- Progress and outcome messages should remain readable for screen readers and compact desktop layouts.

## API / contract changes

- The frontend should consume capability and mutation DTOs from `IStorageClient`; it should not infer account capability from scattered exceptions.
- `StorageConfigForm.razor` is the likely authoring surface for the new mutation opt-in setting.
- The versions tab in `BlobDetailPane.razor` is the natural home for compare and recover actions because it already owns version visibility.

## Tasks

### Wave 1 - Read-only or mutation mode plus upload and copy [blazor-expert]

- [ ] Add mutation-enable affordance to `StorageConfigForm.razor`.
- [ ] Add read-only and mutation-enabled page banners to `StoragePage.razor`.
- [ ] Add upload and copy dialogs with destination preview, progress, and overwrite confirmation.
- [ ] Add focused bUnit coverage for disabled-versus-enabled mode and confirmation text.

### Wave 2 - Metadata editing and version diff [blazor-expert]

- [ ] Add a metadata editor with before/after diff preview to `BlobDetailPane.razor`.
- [ ] Add version compare actions and a bounded diff viewer to the versions experience.
- [ ] Add explicit fallback UX for binary or oversized blobs.
- [ ] Add component tests for diff rendering, truncated compare behavior, and metadata patch previews.

### Wave 3 - Recovery [blazor-expert]

- [ ] Add version restore and undelete affordances where capability detection allows them.
- [ ] Reuse the confirmation model for recovery and overwrite scenarios.
- [ ] Surface the resulting state cleanly in properties and versions after the action completes.
- [ ] Add tests for recovery visibility, typed confirmation, and post-action refresh behavior.

## Validation

- Component tests: Not started
- Manual UX checks:
- Verify read-only mode remains visually obvious when mutations are disabled.
- Verify typed confirmation only appears for the right production-sensitive flows.
- Verify compare and recovery do not make the versions tab unreadable.
- Verify upload/copy/recovery progress and completion messages stay local to the relevant action.

## Notes

- Apply `docs/pitfalls/blazor-maui.md` directly: guard parameter changes before awaits, dispatch UI updates through `InvokeAsync`, and keep any new Storage component namespace imports explicit.
- The current Storage page already has a useful split between list and detail. Keep mutation UX attached to that structure rather than introducing a second page.
- Multi-select exists in `StorageBlobList.razor`, but bulk mutation is intentionally out of scope for this feature slice.
