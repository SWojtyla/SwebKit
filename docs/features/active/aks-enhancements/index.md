# Feature Overview — AKS Enhancements (Batch 2)

---

title: "Feature Overview — AKS Enhancements Batch 2"
owner: ""
status: "Done"
created: "2026-03-17"
updated: "2026-03-17"

---

## Goal

Improve the day-to-day usability of the AKS page with seven targeted improvements:
correct panel stacking behaviour, better events UX, YAML search, Ingress URL access,
accurate pod resource display, correct Helm history ordering, and CronJob visibility.

## Value

The AKS page is the most-used page in the app. These changes remove concrete friction points:
panels that overflow the grid when multiple are open, no way to search large YAML outputs,
no direct path from ingress host to browser, metrics columns that disappear when the metrics
server is unavailable, and no visibility into CronJobs scheduled in the cluster.

## Scope

**In scope:**

- Side-panel layout redesign: unify all `ResizablePanel` slide-outs into a single flex column so multiple panels never break the grid
- Events panel: integrate at the bottom of the side column as a collapsible inset; collapsed by default
- YAML viewer: add inline text search with match highlighting and count
- Ingress hosts: make each host cell a clickable link that opens in the default browser; add context menu options
- Pod overview: always show CPU and Memory columns; show "—" when the metrics server is unavailable
- Helm history: display most recent revision first (reverse order)
- CronJobs tab: new resource type showing schedule, active count, last schedule/success times, and suspended state

**Out of scope:**

- Editing or triggering CronJobs from the UI
- Custom search highlight colour theming
- Ingress TLS certificate details
- Any changes to other pages

## Dependencies

- `IAksClient` / `DemoAksClient` / `KubernetesAksClient` — existing client layer
- `k8s` NuGet package `BatchV1` API for CronJobs (K8s 1.21+)
- `Microsoft.Maui.ApplicationModel.Launcher` for opening URLs
- `yamlHighlight.js` custom JS for YAML highlighting — extended for search

## Risks & mitigations

- Risk: CSS layout regression in the main grid when the panel column is not open — Mitigation: `side-open` class only applied when `HasAnyPanel` is true; default is single-column
- Risk: `BatchV1` CronJob API unavailable on older clusters — Mitigation: `GetCronJobsAsync` returns empty list on 404/exception; grid shows empty state
- Risk: `Launcher.OpenAsync` not available on all MAUI targets — Mitigation: wrapped in `try/catch`; failure is silent (URL copy still works as fallback)
- Risk: YAML search performance on very large YAML blobs — Mitigation: search runs in JS against already-rendered DOM; no Blazor re-render triggered per keystroke

## Related documents

- Architecture: `docs/architecture/functionalities/aks.md`
- Pitfalls: `docs/pitfalls/blazor-maui.md`
- Batch 1 work is documented in `status.md` under "Completed — Batch 1"

## Quick links

- Status: `status.md`
- Backend plan: `backend.md`
- Frontend plan: `frontend.md`
- Decisions: `decisions.md`
- Tests: `test-plan.md`
