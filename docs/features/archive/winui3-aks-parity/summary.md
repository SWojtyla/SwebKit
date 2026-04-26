# Archive Summary - winui3-aks-parity

---

title: "Archive Summary - winui3-aks-parity"
owner: ""
jira: "not linked"
completed_date: "2026-04-26"
pr: "not linked"
commit: "not captured"

---

## Goal

Close the remaining AKS parity gap in the native WinUI workspace so operators can inspect and act on clusters without relying on the MAUI host for the highest-value resource, diagnostics, and operational flows.

## Delivered

- Added a native shared-primitives resource explorer covering Pods, Deployments, StatefulSets, Services, Ingresses, GatewayClasses, Gateways, HTTPRoutes, Jobs, and CronJobs.
- Added a native detail-pane flow that keeps selected-pod diagnostics available while operators pivot to workload, batch, and edge resources.
- Added selected-resource YAML load across the expanded explorer surface plus edit or apply on the currently supported Deployment, StatefulSet, and Ingress kinds.
- Added ingress analysis, network-policy analysis, workload restart or scale, and Job or CronJob rerun or trigger flows in the native action rail.
- Hardened selected-resource YAML, diagnostics, and mutation flows so page disposal cancels in-flight work and suppresses post-dispose notifications.

## Key decisions

- Treat the delivered explorer, diagnostics, YAML, and first-action surface as the completed native parity slice instead of holding the feature open for every MAUI-only evidence panel.
- Keep namespace quota, pod disruption budget, probe-failure, placement, and Helm preview surfaces as explicit future AKS follow-up if later cutover evidence shows they still matter.
- Keep shell and port-forward lifetime treatment as future follow-up only if live navigation-away evidence shows the current disposal hardening is insufficient.

## Validation performed

- Build validation: `build-winui` passed after the Gateway, batch, YAML, and diagnostics changes.
- Automated tests: `dotnet test .\tests\SwebKit.WinUI.Tests\SwebKit.WinUI.Tests.csproj -c Release --filter "FullyQualifiedName~AksPageViewModelTests"` passed with 9 focused tests.
- Automated coverage includes broader resource loading, pod-selection synchronization, preserved diagnostics state, non-fatal partial-load handling, selected-resource YAML load or apply state, ingress diagnostics state, CronJob-triggered job refresh behavior, and disposal cancellation for selected-resource diagnostics plus restart actions.
- Manual review: future cutover review can still exercise the shipped native AKS baseline, but no remaining manual check blocks close-out of this slice.

## Lessons learned

- AKS parity closes more honestly when the slice is anchored to the high-value explorer and action baseline instead of every MAUI evidence panel the route accumulated over time.
- Disposal and navigation-away hardening needed to be part of the slice itself because selected-resource actions were the most likely place for WinUI state corruption to reappear.

## Follow-up

- Namespace quota, pod disruption budget, probe-failure, placement, and Helm preview surfaces if later cutover evidence shows they are still required — owner: future AKS follow-up
- Deeper live-cluster validation of YAML, mutation, shell, and port-forward lifetime behavior — owner: future AKS follow-up
- Final end-to-end review of the shipped native AKS baseline alongside the wider WinUI cutover review — owner: `winui3-cutover-audit-hardening`

## Archive note

> This file is present because the feature had no Jira ticket. Archive location: `docs/features/archive/winui3-aks-parity/`.