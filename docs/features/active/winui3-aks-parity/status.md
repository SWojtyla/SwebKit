# Status - winui3-aks-parity

---

title: "Status - winui3-aks-parity"
owner: ""
state: "In Progress"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-25"

---

## Quick summary

The native AKS route now includes a shared-primitives resource explorer for Pods, Deployments, StatefulSets, Jobs, CronJobs, Services, Ingresses, GatewayClasses, Gateways, and HTTPRoutes, plus a native detail pane that keeps pod diagnostics available while operators pivot to workload, batch, and edge context.

**Jira:** not linked

**Current focus:** close the remaining advanced AKS parity gaps that still live only in the MAUI side-panel rail.

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
- Added focused WinUI AKS view-model coverage for broader resource loading, pod-selection synchronization, preserved diagnostics state, and non-fatal partial-load handling. The new AKS test file is currently blocked from execution on this branch by an unrelated compile failure in `tests/SwebKit.WinUI.Tests/ServiceBusPageViewModelTests.cs`.
- `build-winui` passes after the new Gateway, batch, YAML, and diagnostics changes.

## Remaining

- Decide which remaining MAUI-only evidence panels still need native cutover value, especially namespace quota, pod disruption budget, probe-failure, placement, and Helm preview surfaces.
- Validate the new native YAML and mutation flows against live AKS permissions and production confirmation expectations.
- Validate remaining operational actions under navigation and cancellation pressure with live AKS data.

## Blockers

- None for this slice.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: `build-winui` passed, AKS source and test files are error-free in editor diagnostics, and the focused `AksPageViewModelTests` execution is currently blocked by an unrelated compile error in `tests/SwebKit.WinUI.Tests/ServiceBusPageViewModelTests.cs`.

## Notes

- AKS should be one of the first domain adopters of the shared layout primitives after Dashboard and Settings.
- The native detail pane now owns the first operational action slice; the remaining work is about whether deeper evidence panels still justify a separate native surface or can stay MAUI-only until cutover.
