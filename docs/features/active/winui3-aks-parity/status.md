# Status - winui3-aks-parity

---

title: "Status - winui3-aks-parity"
owner: ""
state: "In Progress"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-26"

---

## Quick summary

The native AKS route now includes a shared-primitives resource explorer for Pods, Deployments, StatefulSets, Jobs, CronJobs, Helm releases, Services, ConfigMaps, Secrets, Ingresses, GatewayClasses, Gateways, and HTTPRoutes, plus a native detail pane that keeps pod diagnostics available while operators pivot to workload, batch, Helm, and edge context. The AKS page now also uses the compact shared scaffold/context pattern so the explorer reaches the viewport earlier and the selected-resource facts land before operational controls, which brings the native page closer to the MAUI content-first UX. The current parity pass also adds recent events, pod metrics and HPA context, selected-resource URL open/copy actions for Ingress and HTTPRoute resources, and native pod delete from the selected-resource action rail. Full MAUI parity is still open because deeper monitoring, detail-panel, and shortcut surfaces are not finished in WinUI yet.

**Jira:** not linked

**Current focus:** close the remaining MAUI-only AKS operator surfaces while keeping the native page content-first and the selected-resource action lifetime safe under row changes, reloads, and navigation-away pressure.

## Progress checklist

- [x] MAUI versus WinUI AKS gap captured
- [x] Remaining resource-type coverage confirmed for the first native slice
- [x] Diagnostics-card adoption planned against shared primitives
- [x] Operational action parity defined
- [x] Focused validation approach defined
- [x] Docs aligned after implementation begins

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

## Remaining

- Full native parity is still open for the remaining MAUI-only monitoring, detail-panel, and shortcut surfaces.
- Live-cluster validation of YAML, Helm, mutation, shell, port-forward, recent-event, and selected-resource action behavior remains explicit follow-up.

## Close-out checklist

- [ ] Close the remaining MAUI-only AKS parity gaps in WinUI.
- [ ] Keep focused AKS tests and `build-winui` green as the remaining parity slices land.
- [ ] Perform live-cluster validation for the native AKS operator flows.

## Blockers

- No implementation blocker is currently known for the native AKS slice; the remaining work is parity scope plus live-cluster validation.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: touched AKS files were checked with `get_errors`, the AKS slice was reviewed with the `validation-gate` subagent after the recent parity additions, focused `AksPageViewModelTests` passed `18/18`, and `build-winui` is currently green. The compact AKS page-layout pass also validated cleanly in XAML diagnostics.

## Notes

- AKS can continue consuming the current shared layout baseline inside its own slice; only reusable primitive gaps should reopen the layout feature.
- The native detail pane now owns the Helm, evidence, and current resource-action parity slice as well; remaining work is the deeper WinUI monitoring/detail parity plus live-cluster validation once the unrelated build blocker is gone.
