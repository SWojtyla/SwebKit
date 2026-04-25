# Feature Overview - winui3-storage-parity

---

title: "Feature Overview - winui3-storage-parity"
owner: ""
status: "Review"
jira: "not linked"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Close the remaining Azure Storage workspace parity gap in WinUI so operators can finish the established browse, preview, and bulk workflows without falling back to MAUI.

## Value

The native Storage route now covers the cutover-critical parity workflows that are actually reachable today: multi-select ZIP download plus version-history compare, download, and restore when the selected storage profile allows mutations. Existing large-file and binary-preview safeguards remain the baseline, and deleted-blob discovery stays deferred as broader storage recovery work rather than part of this slice.

## Scope

- In scope: ZIP and batch download workflows, version-history handling, large-file and binary-preview hardening, and any remaining browse/detail gaps that still exist only in MAUI.
- In scope: adopting shared detail-pane and state primitives where Storage needs richer preview or bulk-state UX.
- Out of scope: storage backend redesign or new product features beyond current MAUI scope.

## Source surfaces

- MAUI baseline: `src/SwebKit.App/Components/Pages/StoragePage.razor`, `src/SwebKit.App/Components/Pages/StorageConfigForm.razor`
- WinUI target: `src/SwebKit.WinUI/Views/Storage/`, `src/SwebKit.WinUI/ViewModels/Storage/`

## Dependencies

- Shared baselines available from: `docs/features/active/winui3-layout-redesign/`, `docs/features/active/winui3-settings-completeness/`
- Related active feature: `docs/features/active/winui3-cutover-audit-hardening/`
- Functionality baseline: `docs/architecture/functionalities/storage.md`
- Pitfall files that apply: `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`

## Parallel execution contract

- This feature owns `src/SwebKit.WinUI/Views/Storage/` and `src/SwebKit.WinUI/ViewModels/Storage/`.
- It may consume the current shared detail-pane, state, and Settings baselines without waiting on additional global planning work.
- Remaining review work is local to storage validation, not a blocker for parallel domain execution.

## Risks & mitigations

- Risk: binary or large-file workflows degrade performance or clarity in WinUI.  
  Mitigation: validate preview strategy and loading states explicitly instead of treating them as minor polish.
- Risk: batch operations land without clear progress or failure handling.  
  Mitigation: make bulk-progress and partial-failure behavior part of the planned acceptance criteria.

## Related documents

- Cutover umbrella: `docs/features/active/winui3-cutover-audit-hardening/`
- Storage functionality: `docs/architecture/functionalities/storage.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: none created; the completed slice is tracked directly in `status.md` and `test-plan.md`
