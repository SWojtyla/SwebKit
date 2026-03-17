# Status — AKS New Capabilities

---

title: "Status - AKS New Capabilities"
owner: ""
state: "Done"
branch: ""
started: ""
last_updated: "2026-03-17"

---

## Quick summary

All planned improvements complete. Batch 2 (7 UX features) fully implemented and verified.

## Quick links

- [index.md](index.md) — goal, scope, risks
- [backend.md](backend.md) — model/interface/JS changes
- [frontend.md](frontend.md) — page and CSS task list
- [decisions.md](decisions.md) — four key design decisions
- [test-plan.md](test-plan.md) — acceptance scenarios and validation status

**Note:** Feature docs (index, backend, frontend, decisions, test-plan) were written
after the batch 2 implementation was complete, not before. They reflect actual decisions
and implementation rather than a prior plan.

## Progress checklist

- [x] Planning complete
- [x] Design reviewed
- [x] Backend implementation
- [x] Frontend implementation
- [x] Tests (unit / integration / manual)
- [x] Docs aligned (architecture/functionalities/aks.md updated)
- [x] Ready for review

## Completed

**Batch 1 (original capabilities):**

- Feature scoping and planning
- New model types, IAksClient extensions, DemoAksClient and KubernetesAksClient implementations
- Multi-pod log aggregation, StatefulSets tab, ConfigMap/Secret viewer, Container detail, HPA inline status, shell launch

**Batch 2 (7 UX improvements):**

- Helm history reverse order — most recent revision shown first
- YAML search — search bar with highlight + scroll to match, powered by `yamlHighlight.js` JS interop
- Ingress URL click — host cells are clickable buttons that open the URL in the default browser; context menu adds "Open URL in browser"
- Side-panel layout redesign — all panels unified in `aks-panels-col` flex column; multiple panels no longer overflow the grid
- Events panel integrated at bottom of side column as collapsible `aks-events-inset`; collapsed by default
- Pod CPU/Memory always visible — columns shown even when metrics unavailable (shows "—")
- CronJobs tab — `CronJobInfo` model, `IAksClient.GetCronJobsAsync`, `DemoAksClient` with 5 demo entries, `KubernetesAksClient` with real BatchV1 API call, full grid UI with schedule/active/last-schedule/last-success columns and suspended badge

**Batch 2 rev 1 (regression fixes, 2026-03-17):**

- Events moved from bottom inset to full peer panel pane (fills column height, full scroll)
- Right panel column wrapped in `ResizablePanel` — drag-resize restored
- Pod CPU/Memory columns show a colour-coded mini bar (0–500m / 0–512Mi scale) alongside the numeric label
- Events toolbar toggle button added so events can be opened alongside a content panel

**Batch 2 rev 2 (bug fixes, 2026-03-17):**

- Removed `aks-events-collapsed-tab` from else-branch — it was creating a second CSS grid row that stole height from the resource panel (appeared as phantom "events panel at bottom", also caused HPA column to be cramped)
- Toolbar split into two rows: Row 1 = connection status + context + namespace + actions (Events toggle, AutoRefresh, Refresh); Row 2 = resource type tab bar — eliminates toolbar overflow with 8 resource types
- `aks-events-toggle` button replaces reused `aks-resource-tab` style — styled with warning colour when active, distinct from resource type tabs

## Blockers

None.

## Validation

Unit tests: 113/113 passing (`SwebKit.Core.Tests` suite).
Build: `SwebKit.App`, `SwebKit.Core`, `SwebKit.Kubernetes` all clean (0 errors, 2 pre-existing unrelated warnings).
Architecture doc: `docs/architecture/functionalities/aks.md` updated.
