# Status — company-readiness-polish

---

title: "Status - company-readiness-polish"
owner: ""
state: "Planned"
jira: ""
branch: ""
started: "2026-07-03"
last_updated: "2026-07-03"

---

## Quick Summary

Screen-by-screen polish pass before company sharing. No new features — fix sloppy UI, inconsistent primitives, personal content, and known rough edges.

**Current focus:** Screen 1 — Service Bus (user validation pass pending).

## Progress Checklist

### Cross-cutting

- [ ] Decision recorded: `<a>` anchor vs `AppButton` for header navigation links

### Screen 1 — Service Bus

- [ ] Entra auth error: blocked — `https://servicebus.azure.com/` service principal not consented in Portima tenant (tenant admin action required)
- [ ] Configurable default peek count
- [ ] Breadcrumb `sb-breadcrumb-back` and `sb-breadcrumb-btn` raw buttons → AppButton
- [ ] Header action slot audit
- [ ] Empty states consistent and copy-reviewed
- [ ] SB-5 grid audit: DLQ tooltip easter egg ("Sébastien would be proud 🥳") removal
- [ ] Manual visual review (light + dark)

### Screen 2 — AKS

- [x] Remove/neutralise `"Suspiciously fine. — SW"` easter egg → now shows "All pods healthy"
- [x] `System.Diagnostics.Debug.WriteLine` debug leak → replaced with `Logger.LogWarning`
- [x] Connection bar and toolbar controls reviewed for consistency (context/namespace pickers intentional custom inputs)
- [x] Regression: `AppButton Ghost` swap for Refresh overlapped the namespace picker → reverted to `FluentButton Appearance.Stealth`
- [x] Bug: container detail panel stayed open with stale pod after selecting a different row → `CloseContainerDetail()` now called on every `SelectDeployment`/`SelectStatefulSet`/`SelectPod`
- [x] Bug: app froze/became unresponsive when switching resource-type tabs repeatedly → BL-4 fix: `@switch` (destroy/recreate grids) replaced with always-mounted `<div hidden>` wrappers so `FluentDataGrid` virtualization isn't torn down and rebuilt every click
- [ ] TODO: Raw `<input type="checkbox">` "Show completed" filter — tight CSS integration, defer to user review
- [ ] TODO: Side panel animation — verify at runtime (no code issue found)
- [ ] Manual visual review (light + dark)
- [ ] Manual perf check: rapidly click through all resource-type tabs, confirm no freeze/lag

### Screen 3 — Redis

- [ ] Replace `FluentButton` in key tree panel → AppButton (load more, etc.)
- [ ] Panel heading `<h2>Keys</h2>` — review styling consistency
- [ ] Workspace status bar (`redis-workspace-status`) — ensure consistent pill/label style
- [ ] Manual visual review (light + dark)

### Screen 4 — Storage

- [ ] `<select class="storage-select">` account picker → AppSelect
- [ ] Header action slot audit
- [ ] StorageMutationBanner — review prominence and copy
- [ ] Manual visual review (light + dark)

### Screen 5 — Monitoring

- [ ] Full audit (not yet read in detail — first pass needed)
- [ ] Manual visual review (light + dark)

### Screen 6 — AI Agent (Sebski panel)

- [ ] `<button class="top-bar-icon-btn agent-panel-header-btn">` → AppIconButton
- [ ] Confirm/cancel clear-history button row — polish
- [ ] Empty state copy review ("What's going on in your cluster?" — ok for company use?)
- [ ] Manual visual review (light + dark)

### Screen 7 — Settings

- [ ] Nav sidebar `<button>` items — review active-state pattern consistency
- [ ] Section header area (RoutePageHeader) — consistent subtitle copy per section
- [ ] Health/readiness report display — confirm it looks intentional
- [ ] Manual visual review (light + dark)

### Excluded screens — rework banner

- [ ] Pipelines: add visible "under rework" notice to the page
- [ ] Observability: add visible "under rework" notice to the page
- [ ] Incident Timeline: add visible "under rework" notice to the page
- [ ] API Client: add visible "under rework" notice to the page

### Final gate

- [ ] Build passes (`dotnet build`)
- [ ] All existing tests pass (`dotnet test`)
- [ ] No new raw button/select regressions in inventory (`scripts/style-inventory.ps1`)
