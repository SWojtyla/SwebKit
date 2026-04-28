# Status - winui3-aks-parity

---

title: "Status - winui3-aks-parity"
owner: ""
state: "In Progress"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-27"

---

## Quick summary

The WinUI AKS route has broad native capability coverage, but it is not ready to be treated as fully parity-complete. The current remaining work is a parity-and-regression pass focused on the concrete operator issues surfaced after the recent layout rewrite: saved context/namespace restore, namespace search, warning noise, log ergonomics, compact page structure, and row-level action discoverability.

**Jira:** not linked

**Current focus:** finish the remaining MAUI parity and regression fixes in the native AKS page, then validate that surface in focused tests and on a live cluster.

## Progress checklist

- [x] MAUI versus WinUI AKS gap captured
- [x] Remaining resource-type coverage confirmed for the first native slice
- [x] Diagnostics-card adoption planned against shared primitives
- [x] Operational action parity defined
- [x] Focused validation approach defined
- [x] Docs aligned after implementation begins
- [x] Remaining regression complaints captured as explicit scope

## Completed

- Confirmed that AKS no longer blocks the baseline routed WinUI host.
- Identified AKS as one of the highest refactor-pressure pages because diagnostics patterns repeat across the view.
- Implemented a native resource explorer for Pods, Deployments, StatefulSets, Services, and Ingresses using the shared `SectionCard`, `MetricCard`, `StateView`, and `DetailPaneHost` primitives.
- Kept selected-pod logs, shell launch, and port-forward sessions available while browsing non-pod resources through the new explorer/detail flow.
- Extended the native explorer to cover GatewayClasses, Gateways, HTTPRoutes, Jobs, and CronJobs without adding a second AKS navigation path.
- Added a native selected-resource action rail for YAML load across the expanded explorer surface, YAML edit/apply on the currently supported Deployment/StatefulSet/Ingress kinds, ingress analysis, network-policy analysis, workload restart/scale, and Job/CronJob rerun or trigger flows.
- Added focused WinUI AKS view-model coverage for broader resource loading, pod-selection synchronization, preserved diagnostics state, non-fatal partial-load handling, and disposal cancellation for selected-resource diagnostics plus restart flows.
- `build-winui` passes after the new Gateway, batch, YAML, and diagnostics changes.
- Hardened selected-resource YAML, diagnostics, and mutation flows so page disposal cancels in-flight AKS calls, suppresses post-dispose notifications, and avoids follow-on refresh work after navigation away.
- Added native Helm browsing to the WinUI explorer together with Helm history, values, upgrade preview, rollback preview, and rollback actions in the selected-resource detail pane.
- Added native namespace quota, pod disruption budget, workload probe-failure, and workload placement evidence flows to the WinUI selected-resource detail pane, reusing the existing action lifetime guard.
- Extended focused WinUI AKS view-model coverage for Helm panels plus the new namespace and workload evidence paths.
- Added native ConfigMap and Secret resource coverage to the WinUI explorer/detail flow.
- Enriched native pod, deployment, and stateful set detail facts with pod metrics and HPA context.
- Added a native recent-events section with warning counts and an expand/collapse action in the WinUI AKS page.
- Added selected-resource URL open/copy actions for Ingress and HTTPRoute resources in the native detail pane.
- Added native pod delete to the selected-resource action rail.
- Hardened selected-resource async work so changing the selected resource invalidates in-flight YAML, diagnostics, Helm, and mutation commands before they can land on a different row.
- Compacted the native AKS header and context chrome so the explorer stays closer to the top of the page, and reordered the detail pane so selected-resource facts land before the action rail.
- Added a native AKS monitor panel that mirrors the MAUI flow for queuing namespaces, starting or stopping pod-health monitoring, and reviewing recent pod alerts without leaving the AKS route.
- Kept the native monitor manager on the shared `IPodHealthMonitorService`, so live and demo mode both exercise the same persistence and tray/dashboard downstream paths.
- Extended focused WinUI AKS view-model coverage for monitor-state hydration, namespace management, and start/stop monitoring commands.
- Added shared monitor-state broadcasts so the WinUI AKS page and dashboard stay in sync even when monitoring is changed from another native surface.
- Added a native workload-log surface for Deployments and StatefulSets so WinUI can stream the same aggregated all-pod logs that the MAUI detail panels expose.
- Added WinUI AKS keyboard shortcuts and visible hint chips so operators can drive logs, YAML, analysis, restart, shell, port-forward, Helm, and deselect flows from the keyboard like the MAUI page.
- Extended focused WinUI AKS view-model coverage for workload-log hydration and the new keyboard shortcut path.
- Reframed the feature as an active parity-and-fix slice instead of treating the current page as already complete apart from live-cluster validation.

## Remaining

- Confirm that first-load context and namespace restore reliably honor persisted settings.
- Keep namespace search restored and validate that it is usable on larger namespace lists.
- Keep the compact AKS page layout free from wasted header chrome, oversized inactive panels, and unnecessary vertical scroll.
- Ensure partial-load warnings stay suppressed when the explorer already has usable data, while still surfacing blocking failures.
- Complete the remaining agreed MAUI action parity audit for row-level and selected-resource actions.
- Validate the current workload-log and pod-log UX against the fullscreen-leaning investigation goal instead of the earlier split-panel compromise.
- Perform live-cluster validation of YAML, Helm, mutation, shell, port-forward, events, monitoring, row actions, and logs after the parity fixes settle.

## Close-out checklist

- [x] Close the remaining MAUI-only AKS detail-panel and shortcut parity gaps in WinUI.
- [ ] Close the concrete operator regressions introduced or exposed during the compact layout rewrite.
- [ ] Keep focused AKS tests and `build-winui` green as the remaining parity slices land.
- [ ] Perform live-cluster validation for the native AKS operator flows, including the native monitor panel.

## Blockers

- No code blocker is currently known, but the current docs were ahead of reality and had to be pulled back from an implied "done except validation" position.
- Full parity sign-off remains blocked on live-cluster review, because demo mode cannot prove every action and scope-restore path.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: keep `build-winui` and focused `AksPageViewModelTests` green while the startup restore, warning behavior, row actions, and log UX slices finish. The latest known build failure was an output-file lock from a running `SwebKit.WinUI.exe`, not a confirmed AKS code regression.

## Notes

- AKS can continue consuming the current shared layout baseline inside its own slice; only reusable primitive gaps should reopen the layout feature.
- The native detail pane remains the main parity vehicle, but row-level discoverability now needs the same explicit attention as the side pane.
- Demo mode is now an explicit AKS monitor showcase and a fast regression path for the native monitoring surface before live-cluster validation.
