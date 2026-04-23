# Status - winui3-migration

---

title: "Status - winui3-migration"
owner: ""
state: "Planned"
jira: "not linked"
branch: "winui-rewrite"
started: ""
last_updated: "2026-04-23"

---

## Quick summary

Phase 0 complete. WinUI 3 blank window boots clean from VS Code using VS MSBuild.

**Jira:** not linked

**Current focus:** Phase 1 — Shell (NavigationView + TabView, port TabService / CommandRegistry / OperatorWorkspaceService).

## Progress checklist

### Phase 0 — Blank shell ✅

- [x] Create `src/SwebKit.WinUI/SwebKit.WinUI.csproj` (WinUI 3, `net10.0-windows10.0.19041.0`)
- [x] Add to `SwebKit.slnx` under `/src/` folder
- [x] Wire `Microsoft.Extensions.Hosting` DI host (replaces `MauiApp.CreateBuilder`)
- [x] Reference all 6 integration projects + `SwebKit.Core`
- [x] Register all existing singletons/transients from `MauiProgram.cs`
- [x] Boot to a blank `MainWindow` — no crash, no missing services
- [x] One-line fix: replace `Microsoft.Maui.Controls.Application.Current` in `WindowsTrayLifecycleService.cs`
- [x] Add `.vscode/launch.json` + `tasks.json` — build via VS MSBuild, debug via `coreclr`

### Phase 1 — Shell

- [ ] `MainWindow` with `NavigationView` and `TabView`
- [ ] Port `TabService` as a ViewModel-layer service
- [ ] Port `CommandRegistry` (keyboard shortcuts without JS interop)
- [ ] Port `OperatorWorkspaceService` — search, recents, favorites
- [ ] Command palette (`AutoSuggestBox`-based flyout)
- [ ] Shell-level notification area
- [ ] Settings page (profile select, theme, user settings)
- [ ] Profile and UI state persistence verified (same JSON repos)

### Phase 2 — ServiceBus

- [ ] Namespace connect page/ViewModel
- [ ] Queue/topic entity tree
- [ ] Message browse, peek, DLQ, send, abandon

### Phase 3 — AKS

- [ ] Cluster connect and namespace selector
- [ ] Pod list grid with health column
- [ ] Pod logs panel
- [ ] Port-forward session management
- [ ] Pod shell (terminal via WebView2 or Windows Terminal integration)

### Phase 4 — Redis

- [ ] Key browser with type icons
- [ ] Value inspector (string, hash, list, set, sorted set)

### Phase 5 — Storage

- [ ] Container browser
- [ ] Blob list and download

### Phase 6 — Pipelines / Releases / Approvals

- [ ] Pipeline tree and run detail
- [ ] Release records list and detail
- [ ] Approval center

### Phase 7 — Observability

- [ ] Resource picker (App Insights discovery)
- [ ] Overview / Failures / Performance / Logs / Availability tabs
- [ ] LiveCharts2 charts replacing ApexCharts
- [ ] Monaco editor in WebView2 for KQL / log output

### Phase 8 — Incident Timeline

- [ ] Workbench toolbar and time window selector
- [ ] Timeline event list
- [ ] Evidence detail panel
- [ ] Snapshot export

### Phase 9 — Cutover

- [ ] All feature domains verified working in `SwebKit.WinUI`
- [ ] E2E tests updated for WinUI host
- [ ] `SwebKit.App` csproj removed from solution
- [ ] `Platforms/` folder cleanup
- [ ] `codebase-guide.md` updated (entry points now in `SwebKit.WinUI`)
- [ ] `architecture.md` updated

## Completed

_(nothing yet)_

## Remaining

All of the above.

## Blockers

None currently identified.

## Validation status

Not started.
