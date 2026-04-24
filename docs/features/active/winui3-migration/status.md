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

Phase 1 shell baseline and Phase 2 Service Bus baseline are in place. Phase 3 AKS still carries the active diagnostics follow-up work, Phase 4 Redis plus Phase 5 Storage have native WinUI baseline routes, and Track 4 has now started in earnest: Pipelines and Observability both have native WinUI baseline routes wired into the shell. The new Pipelines surface covers project scope, pipeline/activity/release/approval tab baselines, while the new Observability surface covers discovery, provider activation, five-tab baseline state, guided/advanced logs execution, and workspace restore. The UI-foundation work now spans both page and shell primitives: semantic resource dictionaries, curated WinUI themes, a global theme coordinator, shared page scaffolds, reusable shell header/banner/status/workspace primitives, and deeper adoption in the newly added Track 4 workspaces.

**Jira:** not linked

**Current focus:** harden the new Pipelines and Observability baseline routes on the shared scaffold, finish the rest of Phase 3 AKS slice 2, and keep pushing the remaining shared page/detail primitives so later parity work does not drift back into one-off XAML.

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
- [x] Pod logs panel
- [ ] Port-forward session management
- [ ] Pod shell (terminal via WebView2 or Windows Terminal integration)

### Phase 4 — Redis

- [x] Key browser with type icons
- [x] Value inspector (string, hash, list, set, sorted set)

### Phase 5 — Storage

- [x] Container browser
- [x] Blob list and download

### Phase 6 — Pipelines / Releases / Approvals

- [x] Pipeline/project scope baseline and tab shell
- [x] Release records and approval summary baseline
- [ ] Approval mutations, tag manager, and deeper parity

### Phase 7 — Observability

- [x] Resource picker (App Insights discovery)
- [x] Overview / Failures / Performance / Logs / Availability tabs
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
- Phase 3 AKS now includes a bounded slice-2 logs seam in `SwebKit.WinUI`: each pod row exposes a native diagnostics entry point, plus container/range/live/filter controls backed by `IAksClient.StreamPodLogsAsync`.
- The AKS WinUI page now schedules its initial bootstrap after the page is loaded and yields once after loading-state transitions so the route can paint before cluster work begins.
- The AKS diagnostics surface now stays compact until a pod is selected, instead of rendering the full empty log viewer all the time.
- WinUI startup now loads `UserSettingsRepository` before shell activation and applies the persisted theme globally through `ThemeCoordinator`.
- `ThemeCoordinator` now forces a WinUI resource refresh when switching between curated themes that stay within the same dark/light family, fixing no-op visual updates for dark-to-dark and light-to-light theme swaps.
- `App.xaml` now carries semantic token, shared-style, and curated-theme resource dictionaries instead of relying only on the default WinUI resource merge.
- `Settings`, `ServiceBus`, and `AKS` now share a `PageScaffold` header/layout primitive and the first semantic surface-card styles, providing the initial proving ground for the broader UI architecture pass.
- `MainWindow` now hosts reusable shell chrome in `SwebKit.WinUI`: a context header, banner strip, status strip, workspace hub, and notification-history surface backed by a dedicated `ShellChromeViewModel` and existing shell/core services.
- The shell now surfaces production/demo/profile-recovery cues from `AppStateService`, current route context, connection state, favorites, recents, and persisted notification history without introducing new domain services.
- Phase 4 Redis now has a native WinUI baseline route in `SwebKit.WinUI`: cache selection, paged key scan/tree browse, typed key details, TTL flows, rename/delete, and common value editors for string/hash/list/set/sorted-set data.
- Phase 5 Storage now has a native WinUI baseline route in `SwebKit.WinUI`: account and container browse, hierarchical blob listing with breadcrumbs, blob detail/metadata/tags, text-friendly preview, download, and URL/SAS copy flows.
- The shared shell route map now activates the new Redis and Storage pages directly instead of sending those areas to `PlaceholderPage`.
- Phase 6 Pipelines/Releases/Approvals now has a native WinUI baseline route in `SwebKit.WinUI`: project scope selection, delivery metrics, and a tabbed baseline for pipelines, activity, releases, and approvals.
- Phase 7 Observability now has a native WinUI baseline route in `SwebKit.WinUI`: Application Insights resource discovery, provider activation, five-tab workspace state, guided and advanced logs execution, and workspace restore.
- The shared shell route map now activates the new Pipelines and Observability pages directly instead of sending those areas to `PlaceholderPage`.
- `SwebKit.WinUI` now provides a native `IObservabilityProviderFactory` implementation so the WinUI host no longer depends on the MAUI-only registration path for observability provider creation.

## Remaining

- Phase 3-9 work listed above.
- Shell/dashboard/settings/theme parity items identified by the 2026-04-24 audit remain open until they are explicitly delivered and validated in `SwebKit.WinUI`.
- Dashboard parity and deeper page-state/detail-pane primitives are still outstanding; much of the downstream workspace composition remains inline beyond the first proving-ground adoption.
- Redis parity still needs the later-phase health/prefix tooling, slow-log/deeper analysis surfaces, and broader bulk-operation coverage called out in `frontend.md`.
- Storage parity still needs the later-phase bulk ZIP/version-download polish and any remaining large-file or binary-preview hardening called out in `frontend.md`.
- Pipelines parity still needs deeper tree/detail behavior, inline mutations, tag-manager workflows, and richer release/approval action coverage from `frontend.md`.
- Observability parity still needs Monaco, LiveCharts2, saved-query polish, drill-through depth, and the richer explainer/investigation flows called out in `frontend.md`.

## Recommended next sequence

- Next: harden Pipelines/Observability on the shared scaffold while finishing AKS slice 2 and the remaining shared page/detail primitives. This is the highest-value path because it reduces rework before the later parity wave and before Incident Timeline starts depending on these routes.
- After that: close the remaining Monaco and chart-hosting seams for Observability and push deeper approval/release parity in Pipelines.
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
- `build-winui` succeeded on 2026-04-24 after the same-family theme refresh fix and the AKS pod-log slice.
- `build-winui` succeeded on 2026-04-24 after the AKS first-paint loading fix and the compact selected-pod diagnostics layout update.
- `build-winui` succeeded on 2026-04-24 after the Redis and Storage baseline slices were wired into shared navigation and DI.
- `build-winui` succeeded on 2026-04-24 after the native Pipelines and Observability baseline routes were wired into shared navigation, the WinUI observability provider-factory seam landed, and the activation-time Observability loading-state polish was applied.
