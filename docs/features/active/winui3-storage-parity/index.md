# Feature Overview - winui3-storage-parity

---

title: "Feature Overview - winui3-storage-parity"
owner: ""
status: "Planned"
jira: "not linked"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Close the remaining Azure Storage workspace parity gap in WinUI so operators can finish the established browse, preview, and bulk workflows without falling back to MAUI.

## Value

The native Storage route already covers account, container, and blob browsing plus SAS copy and text-friendly preview. The remaining MAUI-only value is broader batch handling, download and version workflows, and better large-file or binary-preview behavior. This feature isolates those gaps.

## Scope

- In scope: ZIP and batch download workflows, version-history handling, large-file and binary-preview hardening, and any remaining browse/detail gaps that still exist only in MAUI.
- In scope: adopting shared detail-pane and state primitives where Storage needs richer preview or bulk-state UX.
- Out of scope: storage backend redesign or new product features beyond current MAUI scope.

## Source surfaces

- MAUI baseline: `src/SwebKit.App/Components/Pages/StoragePage.razor`, `src/SwebKit.App/Components/Pages/StorageConfigForm.razor`
- WinUI target: `src/SwebKit.WinUI/Views/Storage/`, `src/SwebKit.WinUI/ViewModels/Storage/`

## Dependencies

- Prerequisite active features: `docs/features/active/winui3-layout-redesign/`, `docs/features/active/winui3-settings-completeness/`
- Related active feature: `docs/features/active/winui3-cutover-audit-hardening/`
- Functionality baseline: `docs/architecture/functionalities/storage.md`
- Pitfall files that apply: `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`

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
- Implementation modules: none yet
