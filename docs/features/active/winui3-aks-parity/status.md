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

**Current focus:** validate the remaining live-cluster parity gaps while keeping selected-resource actions and diagnostics resilient under navigation-away and disposal pressure.

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

## Remaining

- Decide which remaining MAUI-only evidence panels still need native cutover value, especially namespace quota, pod disruption budget, probe-failure, placement, and Helm preview surfaces.
- Validate the new native YAML and mutation flows against live AKS permissions and production confirmation expectations.
- Validate the hardened selected-resource action flows under live AKS navigation-away conditions, including whether pod shell or port-forward commands need the same lifetime treatment.

## Blockers

- None for this slice.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: focused `dotnet test .\tests\SwebKit.WinUI.Tests\SwebKit.WinUI.Tests.csproj -c Release --filter "FullyQualifiedName~AksPageViewModelTests"` passed with 9 tests after the disposal-hardening change.

## Notes

- AKS can continue consuming the current shared layout baseline inside its own slice; only reusable primitive gaps should reopen the layout feature.
- The native detail pane now owns the first operational action slice; the remaining work is about whether deeper evidence panels still justify a separate native surface or can stay MAUI-only until cutover.
