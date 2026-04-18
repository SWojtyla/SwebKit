# Status - storage-controlled-mutations

---

title: "Status - storage-controlled-mutations"
owner: "GitHub Copilot"
state: "In Progress"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-18"

---

## Quick summary

Waves 1, 2, and 3 are complete. Three new components (`BlobMetadataEditor`, `BlobVersionDiffPane`, `BlobRecoveryPanel`) are wired into `BlobDetailPane.razor`. All 364 unit tests pass with 0 errors and 0 warnings.

Jira: not linked

Current focus: pre-ship review and merge.

## Progress checklist

### Planning

- [x] Narrowed the feature to single-blob, operator-initiated mutations with explicit safety controls.
- [x] Chosen read-only-by-default behavior with per-account mutation enablement.
- [x] Captured likely source, UI, and test touchpoints.

### Wave 1 - Mutation safety plus upload and copy

- [x] Extend storage configuration with an additive mutation opt-in field.
- [x] Add upload and same-account copy client contracts plus demo support.
- [x] Add focused mutation dialogs and progress or confirmation flows in the Storage page.
- [x] `StorageMutationBanner.razor` — read-only info vs. mutation-enabled warning with account name.
- [x] `BlobUploadDialog.razor` — file picker, editable blob name, overwrite toggle + warning text, guarded Confirm.
- [x] `BlobCopyDialog.razor` — source label, destination container select, blob name, overwrite toggle + warning, Confirm.
- [x] `StoragePage.razor` wired: banner, Upload/Copy buttons (mutations-gated), inline success/error, BL-2 compliant.
- [x] 6 bUnit tests in `StorageMutationTests.cs` — all passing, 0 errors 0 warnings.

### Wave 2 - Metadata update and version diff

- [x] `BlobMetadataEditor.razor` — editable key/value rows, Remove button, Add key, diff preview (Added/Removed/Changed with text labels), Save gated on HasChanges, ReadOnly mode hides all mutation controls.
- [x] `BlobVersionDiffPane.razor` — empty state, metadata diff table with text labels, size comparison, text diff or "not available" notice.
- [x] `BlobDetailPane.razor` — `AllowMutations` parameter, "Edit metadata" button and Cancel, `SaveMetadataAsync` wired to `SetBlobMetadataAsync`, Compare button per version row, `CompareVersionAsync` wired to `GetVersionComparisonAsync`.
- [x] 7 bUnit tests covering ReadOnly no-Save, Added label, Removed label, empty state, content-compare not available, metadata diff labels.

### Wave 3 - Recovery

- [x] `BlobRecoveryPanel.razor` — restore section (gated on `CanRestore` + version selected), undelete section (gated on `SoftDeleteEnabled`), text "not available" notices where capability absent.
- [x] `BlobDetailPane.razor` — `BlobRecoveryPanel` wired with capabilities, `RestoreVersionAsync`, `UndeleteAsync`.
- [x] `LoadCapabilitiesAsync` called lazily when versions tab is selected.
- [x] 3 bUnit tests: restore not available, restore shown, soft-delete shown.

## Completed

- All three waves implemented and tested (364 tests, 0 failures, 0 warnings).
- All mutation actions gate on `AllowMutations`; read-only mode is unchanged.
- Text labels alongside all color cues (BL-1, BL-2, BL-3 observed throughout).
- New components registered in `SwebKit.App.Tests.csproj` `<RazorComponent>` item group.

## Remaining

- Implement the mutation policy and Wave 1 upload/copy flow.
- Implement Wave 2 metadata and diff support.
- Implement Wave 3 recovery behavior and capability handling.
- Update related docs when code lands.

## Blockers

- Jira ticket is not linked (informational).
- Recovery value depends on account capabilities such as blob versioning or soft delete; the plan assumes those capabilities will be detected and surfaced rather than required everywhere.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- Bulk or wildcard mutations stay out of scope for this feature.
- Typed confirmation is required anywhere the action can overwrite or recover content in a production environment.
- If `AllowMutations` is false, the page should remain visually and behaviorally read-only.
