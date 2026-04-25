# Frontend - WinUI cutover coordination

## Scope

This module now coordinates the split WinUI migration plan rather than owning every remaining implementation detail itself. It keeps the dependency order, the already-landed hardening evidence, and the cutover-critical gaps visible in one place.

## Architecture touchpoints

- Project: `src/SwebKit.WinUI/`
- Shell entry points: `src/SwebKit.WinUI/App.xaml.cs`, `src/SwebKit.WinUI/MainWindow.xaml`, `src/SwebKit.WinUI/MainWindow.xaml.cs`
- Shared shell controls: `src/SwebKit.WinUI/Controls/Shell/`
- Current shared page primitives: `src/SwebKit.WinUI/Controls/Shared/PageScaffold.xaml`, `src/SwebKit.WinUI/Views/Shared/DeferredPageLoadScheduler.cs`
- Active high-pressure routes: `src/SwebKit.WinUI/Views/Dashboard/`, `src/SwebKit.WinUI/Views/Settings/`, `src/SwebKit.WinUI/Views/Aks/`, `src/SwebKit.WinUI/Views/Pipelines/`, `src/SwebKit.WinUI/Views/Observability/`

## Baseline evidence retained by this umbrella

- The WinUI host is already a real native route shell for Dashboard, Settings, Service Bus, AKS, Redis, Storage, Pipelines, and Observability.
- Shared shell primitives exist for banner, status, context header, notification history, and workspace hub.
- Repeated initial-load scheduling has already been consolidated through `DeferredPageLoadScheduler`.
- Pipelines and Observability already map known environment failures into explicit readiness states with native `Open Settings` actions.
- `App.xaml.cs` now logs the real unhandled WinUI exception before the generated debug hook stops in `App.g.i.cs`.

## Active feature split

| Feature                            | Purpose                                                                                 | Depends on               | Cutover critical |
| ---------------------------------- | --------------------------------------------------------------------------------------- | ------------------------ | ---------------- |
| `winui3-layout-redesign`           | Shared page-state and card/layout primitives plus Dashboard and Settings frame adoption | baseline hardening only  | Yes              |
| `winui3-settings-completeness`     | Restore native configuration and repair surfaces for in-scope domains                   | `winui3-layout-redesign` | Yes              |
| `winui3-service-bus-parity`        | Remaining advanced Service Bus workflows                                                | layout + settings        | Medium           |
| `winui3-aks-parity`                | Broader AKS diagnostics and action parity                                               | layout + settings        | Yes              |
| `winui3-redis-parity`              | Redis analytics and bulk-workflow parity                                                | layout + settings        | Medium           |
| `winui3-storage-parity`            | Storage batch, version, and preview-depth parity                                        | layout + settings        | Medium           |
| `winui3-pipelines-releases-parity` | Pipelines workflow parity plus seam reduction                                           | layout + settings        | Yes              |
| `winui3-observability-parity`      | Observability workflow parity plus seam reduction                                       | layout + settings        | Yes              |

## Coordination rules

- Do not move implementation backlog back into this umbrella once a split feature owns it.
- Treat layout redesign as the first execution slice, because shared page-state and card primitives are still missing.
- Treat compact headers and content-first proportions as a global rule, not a Dashboard-only cleanup. Downstream features should assume that top-of-page chrome is constrained and that main work surfaces get priority.
- Treat Settings completeness as the next dependency slice, because the environment-sensitive routes need a native repair path.
- Preserve the current readiness-hardening behavior while Pipelines and Observability keep evolving.
- Keep demo-mode validation separate from live-environment validation when recording cutover evidence.

## Open cutover-critical gaps

- Shared page-state and card primitives are still missing beyond `PageScaffold`.
- Current page proportions still over-invest in header and context chrome instead of the main task surface.
- Native Settings still needs full domain coverage.
- AKS, Pipelines, and Observability still carry the highest page-seam and parity pressure.
- Manual smoke validation for the full native host is still not recorded.

## Implementation tasks

- [x] Capture the remaining work as split active features instead of one monolithic wave plan.
- [ ] Keep the dependency order and cutover-critical labels current as the split features evolve.
- [ ] Record which open gaps are deferred versus still blocking cutover.
- [ ] Run the final native-host smoke gate once the dependency features land.
- [ ] Convert the results into the explicit cutover recommendation.

## Validation notes

- The split features own their implementation-specific validation.
- This umbrella owns the final integration pass: route walkthrough, readiness repair loop, and the final recommendation.
- Keep the current implementation baseline green while the split features land: `build-winui`, `tests/SwebKit.WinUI.Tests`, and `tests/SwebKit.DevOps.Tests`.
