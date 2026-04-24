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

The baseline WinUI migration is now broad enough to checkpoint: native routes exist for dashboard, settings, Service Bus, AKS, Redis, Storage, Pipelines, and Observability, and the shell/dashboard path no longer depends on the MAUI host. Further scope expansion should pause here. The remaining parity audit, hardening, structural refactors, and cutover-readiness work now move under `docs/features/active/winui3-cutover-audit-hardening/` so this feature can act as the baseline checkpoint instead of an ever-widening umbrella.

**Jira:** not linked

**Current focus:** freeze the current migration at a defensible baseline checkpoint, keep `build-winui` green, and route remaining parity/hardening work through `winui3-cutover-audit-hardening` instead of widening this feature further.

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

- [x] `MainWindow` with `NavigationView` (left nav, 8 areas + Settings footer) and `Frame` content host
- [x] Port `TabService` as a plain .NET service (no Blazor deps)
- [x] Port `CommandRegistry` (keyboard shortcuts without JS interop)
- [x] Port `OperatorWorkspaceService` — search, recents, favorites (NavigationManager → `IShellNavigationService`)
- [x] Port the active cutover-scope `IOperatorResourceSearchProvider` implementations (ServiceBus, AKS, Redis, Storage, Observability); the legacy IncidentTimeline provider remains only as shell/search plumbing until later cleanup
- [x] Port `NotificationService`, `SearchScoring`, `ShellErrorPresenter`
- [x] `IShellNavigationService` interface — `MainWindowViewModel` implements it, bridges OperatorWorkspaceService to Frame navigation
- [x] `MainWindowViewModel` — nav state, pane expand/collapse, command palette open/close, persists `IsNavExpanded` to `UiStateRepository`
- [x] `CommandPaletteViewModel` — searches `CommandRegistry`, area-scoped, executes via relay command
- [x] Command palette flyout (Ctrl+K keyboard accelerator — `KeyboardAccelerator` in code-behind)
- [x] Native Dashboard route — readiness summary, cross-workspace health tiles, favorites, recent activity, and pod-health alerts wired as the default landing page
- [x] `PlaceholderPage` — shown for areas not yet migrated (Phases 2-8)
- [x] `SettingsPage` — Appearance (theme ComboBox), General (warm-up toggle and demo-mode toggle), Safety (production toggle); saves to `UserSettingsRepository` + `AppStateService`
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
- [x] Port-forward session management
- [x] Pod shell (Windows Terminal or command-shell launch through `IAksClient.OpenShellAsync`)

### Phase 4 — Redis

- [x] Key browser with type icons
- [x] Value inspector (string, hash, list, set, sorted set)

### Phase 5 — Storage

- [x] Container browser
- [x] Blob list and download

### Phase 6 — Pipelines / Releases / Approvals

- [x] Pipeline/project scope baseline and tab shell
- [x] Release records and approval summary baseline
- [x] Approval mutations with production confirm gating
- [x] Native release tag-manager workflow
- [ ] Deeper release/detail parity

### Phase 7 — Observability

- [x] Resource picker (App Insights discovery)
- [x] Overview / Failures / Performance / Logs / Availability tabs
- [x] Native availability heatmap/list toggle parity over the existing availability result set
- [x] Saved-query run/save/delete baseline over `ObservabilityConfig`
- [ ] LiveCharts2 charts replacing ApexCharts (overview request/failure charts, selected-operation performance trend, and availability summary landed; broader chart parity still remains)
- [ ] Monaco editor in WebView2 for KQL / log output

### Phase 8 — Cutover

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
- Phase 3 AKS now also includes native port-forward start/stop session management in `SwebKit.WinUI`: the selected-pod diagnostics surface exposes a bounded start form plus a tracked session list backed by the shared `IPortForwardSessionService`.
- Phase 3 AKS now also includes native selected-pod shell launch in `SwebKit.WinUI`, reusing `IAksClient.OpenShellAsync` and the same non-sidecar container selection heuristic as the MAUI host.
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
- `SwebKit.DevOps` now includes a bounded approval-enrichment/support fix for the WinUI migration so live approval actions can keep production/unverified safety cues and partial-refresh warning behavior honest.
- The Pipelines approvals tab now supports inline approve/reject actions in `SwebKit.WinUI`, including production CONFIRM gating, per-approval SLA context, and resilient post-submit refresh even when one project approval feed fails.
- The Pipelines releases tab now supports a native release tag-manager workflow in `SwebKit.WinUI`: scoped components load repository tags and recent commits, create annotated tags through `IDevOpsClient`, persist confirmed target tags back into the selected release record, and stay usable in demo mode for migration validation.
- Phase 7 Observability now has a native WinUI baseline route in `SwebKit.WinUI`: Application Insights resource discovery, provider activation, five-tab workspace state, guided and advanced logs execution, and workspace restore.
- The Observability Overview tab now renders native request-volume and failure-rate charts from the existing overview trend payload, extending the WinUI chart seam without changing provider contracts.
- The Observability Performance tab now renders the selected-operation latency trend with LiveCharts2, establishing the first native chart-hosting seam in `SwebKit.WinUI` without changing domain or integration projects.
- The Observability Availability tab now renders an availability-by-test LiveCharts summary above the native results list, extending the chart seam without adding new provider APIs or changing persisted workspace state.
- The Observability Availability tab now also supports a native hourly heatmap/list toggle in `SwebKit.WinUI`, deriving pass-rate buckets from the existing availability result set without changing provider contracts.
- The Observability Logs tab now supports native saved-query run/save/delete flows in `SwebKit.WinUI`, persisting directly through the shared `ObservabilityConfig` without changing provider contracts.
- The Observability Failures tab now supports a focused sample-trace drill in `SwebKit.WinUI`, reusing the provider's sample operation id and the shared `KqlPresets.TraceByOperationId(...)` helper instead of adding a new query contract.
- The shared shell route map now activates the new Pipelines and Observability pages directly instead of sending those areas to `PlaceholderPage`.
- `SwebKit.WinUI` now provides a native `IObservabilityProviderFactory` implementation so the WinUI host no longer depends on the MAUI-only registration path for observability provider creation.
- `SwebKit.WinUI` now has a native dashboard route wired as the default landing page, backed by a WinUI-owned `DashboardPageViewModel`, `ConfigurationProbeService`, and `PodHealthMonitorService` so readiness, favorites, recents, and pod-health alerts no longer depend on the MAUI dashboard during cutover validation.
- The WinUI host now restores a first-class demo-mode path for migration validation: Settings can persist demo-mode on/off through `AppStateService`, and the shell demo banner now exposes a native disable action without falling back to the MAUI top bar.
- The native AKS workspace no longer depends on a missing global `InverseBooleanConverter` resource in the port-forward form; the cancel-button enabled state now comes from the view model directly, fixing the demo-mode route crash during page load.

## Remaining

- Phase 3-8 work listed above.
- Remaining parity audit, hardening, and cutover-readiness work is now tracked in `docs/features/active/winui3-cutover-audit-hardening/`.
- Shell/dashboard/settings/theme parity items identified by the 2026-04-24 audit remain open until they are explicitly delivered and validated in `SwebKit.WinUI`.
- The dashboard baseline route is now native, but deeper shell/settings/theme polish and page-state/detail-pane primitives are still outstanding; much of the downstream workspace composition remains inline beyond the first proving-ground adoption.
- Redis parity still needs the later-phase health/prefix tooling, slow-log/deeper analysis surfaces, and broader bulk-operation coverage called out in `frontend.md`.
- Storage parity still needs the later-phase bulk ZIP/version-download polish and any remaining large-file or binary-preview hardening called out in `frontend.md`.
- Pipelines parity still needs deeper tree/detail behavior, richer release-detail editing/matrix coverage, and the remaining release/approval action depth from `frontend.md`.
- Observability parity still needs Monaco, deeper drill-through/investigation flows, and the richer explainer surfaces called out in `frontend.md`.

## Recommended next sequence

- Next: treat this feature as the baseline checkpoint only, and execute the remaining parity/hardening plan from `docs/features/active/winui3-cutover-audit-hardening/`.
- After that: when the follow-up feature reaches a credible cutover recommendation, return here only to archive or close the baseline migration record.

## Planning updates

- The migration plan now treats Dashboard, shell workspace surfaces, full Settings/readiness flows, and curated theme/look-and-feel parity as cutover scope rather than optional polish.
- The migration plan now also treats reusable UI architecture as first-class scope: resource dictionaries, semantic theming, shell primitives, and page/workspace scaffolds.
- `frontend.md` now carries the detailed per-domain parity checklist for Service Bus, AKS, Redis, Storage, Pipelines, and Observability.
- `frontend.md` now also defines the execution order and the cutover-gate versus parallelization rules for the remaining work.
- `decisions.md` now records the UI foundation-first and semantic-theming decisions so page work does not drift back to ad hoc XAML.
- `test-plan.md` now defines validation for the shared UI foundation, theme behavior, shell consistency, and downstream workspace adoption.
- Round 2 is reserved for extra WinUI-only personalization or post-parity visual polish. Existing MAUI capabilities stay in the current plan.
- The follow-up feature `docs/features/active/winui3-cutover-audit-hardening/` now owns the remaining parity audit, refactoring, hardening, and cutover-readiness work.

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
- `build-winui` succeeded on 2026-04-24 after the Observability Performance tab adopted the first LiveCharts2 trend view in the WinUI host.
- `build-winui` succeeded on 2026-04-24 after the Observability Availability tab adopted a native LiveCharts summary chart over the existing result set.
- `build-winui` succeeded on 2026-04-24 after the Observability Overview tab adopted native request-volume and failure-rate charts over the existing overview trend payload.
- `build-winui` succeeded on 2026-04-24 after the AKS WinUI diagnostics surface adopted native port-forward start/stop session management backed by the shared session service.
- `build-winui` succeeded on 2026-04-24 after the AKS WinUI diagnostics surface adopted native selected-pod shell launch backed by `IAksClient.OpenShellAsync`.
- `build-winui` succeeded on 2026-04-24 after the Pipelines WinUI approvals tab adopted inline approve/reject actions, production CONFIRM gating, and resilient cross-project refresh behavior.
- `build-winui` succeeded on 2026-04-24 after the Observability Availability tab adopted a native hourly heatmap/list toggle over the existing result set.
- `build-winui` succeeded on 2026-04-24 after the Observability Logs tab adopted native saved-query run/save/delete flows over the shared Observability profile state.
- `build-winui` succeeded on 2026-04-24 after the Observability Failures tab adopted a focused sample-trace drill over the existing exception-group payload.
- `build-winui` succeeded on 2026-04-24 after the native dashboard route, WinUI-owned readiness probe service, and pod-health monitor were wired into the shell as the default landing page.
- `build-winui` succeeded on 2026-04-24 after the WinUI Settings page restored demo-mode enablement and the shell banner added a native demo-mode disable action.
- `build-winui` succeeded on 2026-04-24 after the AKS demo-mode crash fix removed the missing `InverseBooleanConverter` dependency from the native port-forward form.
- `build-winui` succeeded on 2026-04-24 after the Pipelines releases tab adopted a native release tag-manager workflow over the existing release records and Azure DevOps git operations.
