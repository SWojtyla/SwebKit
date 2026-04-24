# Status - winui3-migration

---

title: "Status - winui3-migration"
owner: ""
state: "In Progress"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-23"
last_updated: "2026-04-24"

---

## Quick summary

Phase 1 complete. Shell: NavigationView + Frame, all phase 1 services ported, Settings page live.

**Jira:** not linked

**Current focus:** Phase 3 — AKS domain.

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

### Phase 1 — Shell ✅

- [x] `MainWindow` with `NavigationView` (left nav, 7 areas + Settings footer) and `Frame` content host
- [x] Port `TabService` as a plain .NET service (no Blazor deps)
- [x] Port `CommandRegistry` (keyboard shortcuts without JS interop)
- [x] Port `OperatorWorkspaceService` — search, recents, favorites (NavigationManager → `IShellNavigationService`)
- [x] Port all 6 `IOperatorResourceSearchProvider` implementations (ServiceBus, AKS, Redis, Storage, Observability, IncidentTimeline)
- [x] Port `NotificationService`, `SearchScoring`, `ShellErrorPresenter`
- [x] `IShellNavigationService` interface — `MainWindowViewModel` implements it, bridges OperatorWorkspaceService to Frame navigation
- [x] `MainWindowViewModel` — nav state, pane expand/collapse, command palette open/close, persists `IsNavExpanded` to `UiStateRepository`
- [x] `CommandPaletteViewModel` — searches `CommandRegistry`, area-scoped, executes via relay command
- [x] Command palette flyout (Ctrl+K keyboard accelerator — `KeyboardAccelerator` in code-behind)
- [x] `PlaceholderPage` — shown for areas not yet migrated (Phases 2-8)
- [x] `SettingsPage` — Appearance (theme ComboBox), General (warm-up toggle), Safety (production toggle); saves to `UserSettingsRepository` + `AppStateService`
- [x] `SettingsViewModel` — loads/saves all three settings, tracks dirty state
- [x] `ServiceRegistration` updated — all Phase 1 services registered, TODO comments removed
- [x] Build succeeds (0 errors, 12 AOT-compat warnings expected)

### Phase 2 — ServiceBus

- [x] Namespace connect page/ViewModel
- [x] Queue/topic entity tree
- [x] Message browse, peek, DLQ, send, and selected-message DLQ actions

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

- Phase 2 Service Bus surface implemented in `SwebKit.WinUI`.
- WinUI app startup now initializes `AppStateService` before feature pages load persisted config.
- Service Bus page now supports namespace add/remove, queue and subscription exploration, active/DLQ tabs, message detail viewing, send, and single-message DLQ resubmit/complete.
- VS Code workspace WinUI debug is pinned to the RID-specific `bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/SwebKit.WinUI.exe` output because project-level "Debug New Instance" can run the stale parent `bin/x64/Debug/net10.0-windows10.0.19041.0/SwebKit.WinUI.exe`.

## Remaining

- Phase 3-9 work listed above.

## Blockers

None currently identified.

## Validation status

- `build-winui` succeeded on 2026-04-23 after the Phase 2 Service Bus implementation.
