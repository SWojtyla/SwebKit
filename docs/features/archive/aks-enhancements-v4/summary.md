# Archive Summary — AKS Enhancements (Batch 2 / v4)

---

title: "Archive Summary - AKS Enhancements Batch 2"
owner: ""
completed_date: "2026-03-18"
pr: ""
commit: ""

---

## Goal

Improve the day-to-day usability of the AKS page with seven targeted UX improvements:
correct panel stacking, better events UX, YAML search, Ingress URL access, accurate pod
resource display, correct Helm history ordering, and CronJob visibility.

## Delivered

- **CronJobs tab** — `CronJobInfo` model, `IAksClient.GetCronJobsAsync`, `DemoAksClient` with 5 demo entries, `KubernetesAksClient` using BatchV1 API; grid with schedule / active count / last-schedule / last-success columns and suspended badge. Returns empty list on 404/exception for clusters pre-K8s 1.21.
- **Side-panel layout redesign** — all panel panes unified in a single `aks-panels-col` flex column wrapped in one `ResizablePanel` (420px default, 280–900px range); multiple panels no longer overflow the grid.
- **Events panel** — full peer `aks-panel-pane` (fills column height with `flex: 1`); shown/hidden via toolbar toggle button; thin collapsed-tab strip visible when no panel is open. Toggle button uses warning colour when active.
- **YAML search** — inline search bar with match highlighting, match count, and scroll-to-first-match via `yamlHighlight.js` JS interop. Search runs entirely in JS; no Blazor re-render per keystroke.
- **Ingress URL click** — host cells rendered as clickable buttons that open in the default browser via `Launcher.OpenAsync`; context menu adds "Open URL in browser". Failure is silent (URL copy remains as fallback).
- **Pod CPU/Memory always visible** — GPU/Memory columns always rendered; colour-coded mini bar (0–500m / 0–512Mi scale) alongside numeric label; shows "—" and no bar when metrics server is unavailable.
- **Helm history reverse order** — most recent revision shown first.
- **Toolbar split into two rows** — Row 1: connection status + context + namespace + actions (Events toggle, AutoRefresh, Refresh); Row 2: resource type tab bar. Eliminates overflow at 8 resource types.

**Regression fixes applied during iteration:**

- Removed `aks-events-collapsed-tab` from else-branch that stole grid height from the resource panel.
- `aks-events-toggle` button style separated from `aks-resource-tab` style; warning colour when active.

## Key decisions

- **Decision 001 — Single outer ResizablePanel wrapping all panel panes** — individual per-panel `ResizablePanel` components caused a third grid child to overflow onto a new row; wrapping the whole column in one `ResizablePanel` fixes overflow and restores drag-resize with a single handle.
- **Decision 002 — Events as a peer panel pane (not bottom inset)** — bottom inset had constrained scroll area and felt disconnected; peer pane with `flex: 1` gives full column height consistently with YAML and log panels.
- **CronJob API graceful degradation** — `GetCronJobsAsync` returns empty list on any exception to tolerate clusters running Kubernetes < 1.21.
- **Feature docs written post-implementation** — all five feature docs (index, backend, frontend, decisions, test-plan) drafted after the implementation was complete; reflects actual decisions rather than a prior plan.

## Validation performed

- Unit tests: 113/113 passing (`SwebKit.Core.Tests`).
- Build: `SwebKit.App`, `SwebKit.Core`, `SwebKit.Kubernetes` — 0 errors, 2 pre-existing unrelated warnings.
- Architecture doc `docs/architecture/functionalities/aks.md` updated in same change set.
- Manual smoke tests completed during the rev 1 / rev 2 iteration cycle.

## Lessons learned

- Panel overflow bugs are best fixed at the container level — wrapping all panels in one `ResizablePanel` is simpler and more robust than individual `ResizablePanel` per panel.
- Exploratory UX iteration benefits from post-hoc documentation — docs written to reflect actual decisions are more accurate than plans written before implementation.
- Events (and any scrollable content) should be full-height peer panes, not bottom insets. Users notice the height constraint immediately.

## Follow-up

- Editing or triggering CronJobs from the UI (out of scope, future feature).
- Custom YAML search highlight colour theming.
- Ingress TLS certificate detail view.

## Archive metadata

- Active feature folder removed: `docs/features/active/aks-enhancements/`
- Architecture doc: `docs/architecture/functionalities/aks.md`
- Predecessor: `docs/features/archive/aks-enhancements-v3/` (multi-pod logs, StatefulSets, ConfigMaps, HPA, shell)
- Naming: stored as `aks-enhancements-v4` to follow the `v2` / `v3` convention in archive
