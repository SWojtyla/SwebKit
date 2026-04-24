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

Phase 1 shell baseline and Phase 2 Service Bus baseline are in place. Phase 3 AKS slice 1 is now live with cluster bootstrap, context/namespace selection, and a pod health grid. The UI-foundation work now spans both page and shell primitives: semantic resource dictionaries, curated WinUI themes, a global theme coordinator, shared page scaffolds, and the first reusable shell header/banner/status/workspace primitives are now in place.

**Jira:** not linked

**Current focus:** push the new semantic shell/page primitives deeper into the proving-ground workspaces and continue Phase 3 AKS slice 2, now that the first shell-chrome pass is live and validated.

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

### Foundation pass — UI architecture and reusable shell primitives

- [x] Add app-level resource dictionaries for semantic tokens, curated theme dictionaries, and shared styles
- [x] Add a global theme coordinator and expand the current `system` / `dark` / `light` baseline to the curated theme set
- [x] Add reusable shell primitives for title/status chrome, notifications, workspace hub, and banners
- [ ] Add reusable page/workspace primitives for scaffold, section cards, metric cards, state views, and detail-pane layouts
- [ ] Refactor `Settings`, `ServiceBus`, and `AKS` onto the shared primitives as the proving ground before broader page expansion

### Phase 2 — ServiceBus

- [x] Namespace connect page/ViewModel
- [x] Queue/topic entity tree
- [x] Message browse, peek, DLQ, send, and selected-message DLQ actions

### Phase 3 — AKS

- [x] Cluster connect and namespace selector
- [x] Pod list grid with health column
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
- Phase 3 AKS slice 1 is now live in `SwebKit.WinUI`: route wiring, cluster bootstrap, context/namespace selection, and pod browse with a native health/status grid.
- WinUI startup now loads `UserSettingsRepository` before shell activation and applies the persisted theme globally through `ThemeCoordinator`.
- `App.xaml` now carries semantic token, shared-style, and curated-theme resource dictionaries instead of relying only on the default WinUI resource merge.
- `Settings`, `ServiceBus`, and `AKS` now share a `PageScaffold` header/layout primitive and the first semantic surface-card styles, providing the initial proving ground for the broader UI architecture pass.
- `MainWindow` now hosts reusable shell chrome in `SwebKit.WinUI`: a context header, banner strip, status strip, workspace hub, and notification-history surface backed by a dedicated `ShellChromeViewModel` and existing shell/core services.
- The shell now surfaces production/demo/profile-recovery cues from `AppStateService`, current route context, connection state, favorites, recents, and persisted notification history without introducing new domain services.

## Remaining

- Phase 3-9 work listed above.
- Shell/dashboard/settings/theme parity items identified by the 2026-04-24 audit remain open until they are explicitly delivered and validated in `SwebKit.WinUI`.
- Dashboard parity and deeper page-state/detail-pane primitives are still outstanding; much of the downstream workspace composition remains inline beyond the first proving-ground adoption.

## Recommended next sequence

- Next: push the new shell/page primitive set deeper into the current proving-ground workspaces while continuing AKS slice 2 in parallel. This is the highest-value path because it reduces rework before more pages are added.
- After that: Redis and Storage in parallel, once the shared shell surfaces are stable enough to avoid rework.
- Then: Pipelines/Releases and Observability in parallel, after Monaco and chart-hosting seams are fixed for WinUI.
- Then: Incident Timeline, after its upstream workspace dependencies are credible.
- Last: hardening, docs updates, test migration, and cutover.

## Planning updates

- The migration plan now treats Dashboard, shell workspace surfaces, full Settings/readiness flows, and curated theme/look-and-feel parity as cutover scope rather than optional polish.
- The migration plan now also treats reusable UI architecture as first-class scope: resource dictionaries, semantic theming, shell primitives, and page/workspace scaffolds.
- `frontend.md` now carries the detailed per-domain parity checklist for Service Bus, AKS, Redis, Storage, Pipelines, Observability, and Incident Timeline.
- `frontend.md` now also defines the execution order and the cutover-gate versus parallelization rules for the remaining work.
- `decisions.md` now records the UI foundation-first and semantic-theming decisions so page work does not drift back to ad hoc XAML.
- `test-plan.md` now defines validation for the shared UI foundation, theme behavior, shell consistency, and downstream workspace adoption.
- Round 2 is reserved for extra WinUI-only personalization or post-parity visual polish. Existing MAUI capabilities stay in the current plan.

## Blockers

- No delivery blocker is currently identified, but the original feature docs had under-scoped MAUI parity. Phase completion should now be judged against `frontend.md`, not the shorter phase headlines alone.

## Validation status

- `build-winui` succeeded on 2026-04-23 after the Phase 2 Service Bus implementation.
- `build-winui` succeeded on 2026-04-24 after the AKS slice 1 WinUI changes.
- `build-winui` succeeded on 2026-04-24 after the theme/resource foundation and shared page-scaffold slice.
- `build-winui` succeeded on 2026-04-24 after the reusable shell-chrome/workspace-hub/notification-history slice.
