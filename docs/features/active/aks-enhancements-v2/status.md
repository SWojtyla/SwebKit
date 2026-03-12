# Status - AKS Enhancements v2

---

title: "Status - AKS Enhancements v2"
owner: ""
state: "In Progress"
branch: "sw/main/aks"
started: "2026-03-11"
last_updated: "2026-03-12"

---

## Quick summary

Second enhancement phase for AKS: UX improvements (resizable panels, context menus), mutative operations (restart, kill, rollback), and Helm release inspection.

**Current focus:** Final polish — persist panel widths, label selector filtering.

## Progress checklist

- [x] Planning complete
- [x] Design reviewed
- [x] Backend implementation
- [x] Frontend implementation
- [x] Tests (31 unit tests passing)
- [x] Docs aligned
- [ ] Ready for review

## Completed

- Feature scope defined and module docs created.
- **Phase 1: Context menus and confirm bar**
  - Created `ContextMenu.razor` — generic right-click context menu component with backdrop, keyboard dismiss, cursor-positioned menu.
  - Created `AksConfirmBar.razor` — inline confirmation bar with optional typed-name guard for production environments.
  - Replaced all inline Logs/YAML buttons on Deployments, Pods, Ingresses, and Helm tabs with right-click context menus.
  - Added `RestartDeploymentAsync` to `IAksClient` — patches pod template annotation (same as `kubectl rollout restart`). Implemented in both `KubernetesAksClient` and `DemoAksClient`.
  - Added `DeletePodAsync` to `IAksClient` — graceful pod deletion. Implemented in both clients.
  - Wired Restart Deployment and Kill Pod actions through context menus with confirmation (production guard requires typing resource name).
  - Added Copy Host URL action for Ingresses.
- **Phase 2: Mutative operations and Helm inspection**
  - Added `ScaleDeploymentAsync` to `IAksClient` — patches deployment replica count. Wired through context menu with inline replica input.
  - Added `GetHelmReleaseHistoryAsync` — queries Helm secrets for revision history. Wired to history side panel with revision table.
  - Added `GetHelmReleaseValuesAsync` — decodes Helm release secret to extract computed values. Wired to values side panel (read-only YAML).
  - Added `RollbackHelmReleaseAsync` — invokes `helm rollback` via CLI subprocess. Wired through context menu with confirmation (rolls back to most recent superseded revision).
  - Added `GetPodMetricsAsync` — queries Metrics API with graceful fallback (returns empty if unavailable).
  - Added `HelmRevisionInfo`, `PodMetrics`, `ContainerMetrics` models.
  - Updated `DemoAksClient` with demo data for all new methods.
- **Phase 3: Filtering, auto-refresh, and pod metrics**
  - Created `ResourceFilter.razor` — search/filter bar with per-tab independent state, real-time substring matching, and clear button.
  - Added filter bar to all tabs (Deployments, Pods, Ingresses, Helm) — filters by name, status, and relevant fields.
  - Created `AutoRefreshToggle.razor` — toolbar toggle with interval dropdown (10s/30s/60s). Pauses when panels/dialogs are open.
  - Added CPU and Memory columns to Pods tab from `GetPodMetricsAsync`. Columns hidden when Metrics API unavailable. Color-coded: green (low), orange (moderate), red (high).
- **Phase 4: Resizable panels and multi-namespace**
  - Created `ResizablePanel.razor` — draggable split panel with left-edge drag handle, min/max constraints, double-click to reset.
  - Wrapped YAML, Helm History, Helm Values, and Log panels with `ResizablePanel`. Each panel independently resizable.
  - Added multi-namespace overloads for `GetDeploymentsAsync` and `GetPodsAsync` with default interface implementations (parallel execution per namespace).
  - Added "All namespaces" option to namespace dropdown.
  - Added clickable Namespace column to Deployments and Pods grids in multi-namespace mode (click to filter to single namespace).
- **Phase 5: Tests**
  - Added 18 unit tests for `DemoAksClient` covering all new methods (scale, helm history/values/rollback, pod metrics, multi-namespace, events, contexts).
  - Added 13 unit tests for `KubernetesAksClient` CPU/memory parsing helpers (nanocores, millicores, Ki/Mi/Gi).
  - All 31 tests passing.

## Remaining

- Persist panel widths in UI state (minor polish).
- Label selector advanced filtering (minor polish).

## Known Bugs

### BUG-1: "Unsupported resource kind: Helm" when viewing Helm release YAML
- **Symptom:** Clicking "View Release YAML" in the Helm context menu shows error "Unsupported resource kind: Helm" in the side panel.
- **Root cause:** `KubernetesAksClient.GetResourceYamlAsync` handles "deployment", "pod", "ingress", "service" via Kubernetes API but has no case for "helm". Helm releases are not native Kubernetes resources — they require the `helm get manifest` CLI command.
- **Fix:** Add a "helm" case that runs `helm get manifest <release> --namespace <ns>` via subprocess (consistent with other Helm CLI calls in the client).

### BUG-2: Auto-refresh does not update the UI
- **Symptom:** Enabling auto-refresh and switching tabs (e.g., restarting a pod on Deployments, then checking Pods) shows no updates until a manual refresh.
- **Root cause:** `AutoRefreshToggle` uses `System.Threading.Timer` with an `async void` callback. If `LoadAsync` throws a transient error, the exception is unobserved and silently swallowed, breaking subsequent ticks. The `async void` pattern also prevents proper error handling and reentrancy control.
- **Fix:** Replace `System.Threading.Timer` with `PeriodicTimer` + async loop with proper cancellation and error handling.

### BUG-3: Helm rollback doesn't allow choosing a target revision
- **Symptom:** Clicking "Rollback..." always rolls back to the most recent superseded revision with no way to pick an older revision.
- **Root cause:** `OnCtxRollbackHelm` hardcodes `previousRevisions[0].Revision` as the target and goes straight to the confirmation dialog.
- **Fix:** Show the history panel with clickable "Rollback to this revision" buttons on each superseded row, allowing the user to select which revision to rollback to.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Unit tests passing (31 tests)

## Notes

- Mutative operations require production guard (confirmation dialogs).
- Context menus must be custom HTML/CSS (not browser native) for MAUI BlazorWebView compatibility.
