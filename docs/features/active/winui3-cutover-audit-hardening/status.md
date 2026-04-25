# Status - winui3-cutover-audit-hardening

---

title: "Status - winui3-cutover-audit-hardening"
owner: ""
state: "In Progress"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-24"
last_updated: "2026-04-25"

---

## Quick summary

This feature now owns the active WinUI cutover path. The baseline migration checkpoint has been archived, the domain gap matrix is in place, routed pages share deferred first-load scheduling, and the environment-sensitive routes now expose explicit readiness guidance instead of raw auth/access failures.

**Jira:** not linked

**Current focus:** close the remaining cutover gate with a first manual native-host smoke pass, then record an explicit recommendation on whether `SwebKit.App` can be treated as legacy-only.

## Progress checklist

### Wave 0 — Checkpoint and audit

- [x] Related baseline feature identified (`winui3-migration`)
- [x] Current route coverage and shared-primitives surface reviewed
- [x] Runtime blocker evidence gathered from launch behavior and Windows Application event logs
- [x] Domain gap matrix reviewed against current WinUI implementation
- [x] Baseline checkpoint accepted as the new starting point for follow-up work

### Wave 1 — Domain parity closure

- [ ] Shell/dashboard/settings parity gaps closed
- [ ] Service Bus parity gaps closed
- [ ] AKS parity gaps closed
- [ ] Redis parity gaps closed
- [ ] Storage parity gaps closed
- [ ] Pipelines/Releases parity gaps closed
- [ ] Observability parity gaps closed

### Wave 2 — Refactors and hardening

- [ ] Shared state, metric, and detail-pane primitives added
- [x] Repeated WinUI page activation pattern consolidated
- [ ] Oversized page/view-model seams reduced where needed
- [x] Auth/readiness failure handling normalized across Azure-backed pages
- [x] Focused WinUI automated coverage introduced

### Wave 3 — Cutover readiness

- [ ] Manual smoke suite executed against the native host
- [ ] WinUI test/docs updates aligned with the cutover path
- [ ] Architecture docs updated for the new primary host
- [ ] Explicit cutover recommendation recorded

## Completed

- Verified that the WinUI app launches and remains alive; the current blocker is not a launch-time crash.
- Confirmed that the current migration already has native routed pages for dashboard, settings, Service Bus, AKS, Redis, Storage, Pipelines, and Observability.
- Confirmed that shared shell primitives exist, but shared page primitives remain partial: the repo currently has `PageScaffold` plus shell panels, not the broader `StateView` / metric / detail host set.
- Archived the completed `winui3-migration` baseline as a historical checkpoint; this feature is now the only active source of truth for parity, hardening, and cutover readiness.
- Routed first-load scheduling for `Dashboard`, `AKS`, `Redis`, `Storage`, `Pipelines`, and `Observability` now runs through a shared `DeferredPageLoadScheduler` helper instead of six page-local `Loaded += HandleInitialPageLoadAsync` implementations.
- `DashboardPageViewModel` and `AksPageViewModel` now short-circuit deferred load entry after disposal so the shared scheduler cannot resume work against torn-down page state.
- `PipelinesPageViewModel` and `ObservabilityPageViewModel` now map known Azure DevOps connection-validation failures and Azure credential-chain failures into explicit route-level readiness states with an `Open Settings` action instead of presenting raw error text as if the host itself were unstable.
- The native Pipelines and Observability pages now surface those readiness states through in-page `InfoBar` guidance rather than empty-state ambiguity.
- `App.xaml.cs` now logs the real unhandled WinUI exception before the generated debug hook stops in `App.g.i.cs`, so any remaining debugger-break investigation has concrete evidence.
- Added the first focused native-host automated seam in `tests/SwebKit.WinUI.Tests`, covering both readiness formatter classification and the Pipelines/Observability view-model state gating that suppresses contradictory workspace and empty-state UI.

## Remaining

- Decide which open items remain true cutover requirements versus deliberate post-cutover backlog.
- Reproduce the debugger-break path with exact route/action steps if it still occurs under a debugger now that app-level logging captures the underlying exception.
- Execute the first manual native-host smoke pass and record the cutover gate results.
- Build out the shared state, metric, and detail-pane primitives that still sit behind page-local XAML.
- Decide whether any remaining domain-parity gaps block cutover or should move to post-cutover backlog.

## Blockers

- No exact repro steps yet for the debugger-break path beyond the generated `App.g.i.cs` symptom line, although future repros now log the underlying exception in `App.xaml.cs`.
- Live validation of Pipelines and Observability remains environment-sensitive because the current machine state does not provide a successful Azure DevOps or Azure credential path.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: In progress
- `build-winui` succeeded on 2026-04-25 after the Pipelines/Observability readiness-state wiring and the app-level unhandled-exception logging hook landed.
- `dotnet test tests/SwebKit.WinUI.Tests/SwebKit.WinUI.Tests.csproj` succeeded on 2026-04-25 with 8 passing tests covering `WorkspaceReadinessFormatter`, readiness-state view-model gating, and the generic error fallback paths in Pipelines and Observability.

## Notes

- The archived `winui3-migration` checkpoint is now historical reference only; this feature is the source of truth for remaining parity, hardening, and cutover work.
