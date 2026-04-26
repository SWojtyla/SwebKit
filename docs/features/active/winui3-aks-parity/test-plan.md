# Test Plan - winui3-aks-parity

---

title: "Test Plan - winui3-aks-parity"
owner: ""
status: "In Progress"
created: "2026-04-25"
updated: "2026-04-26"

---

## Goal

Validate that the native AKS workspace reaches actual MAUI parity for cluster inspection and day-2 actions while staying stable under asynchronous load, resource switching, and navigation changes, with live-cluster confirmation preserved as the main follow-up.

## Scope

- In scope: broader resource coverage, diagnostics panels, operational actions, recent-event visibility, selected-resource URL actions, shared-card adoption, and content-first native AKS page layout
- Out of scope: new AKS management features unrelated to current MAUI behavior
- Out of scope: new AKS management features unrelated to current MAUI behavior

## Main scenarios (priority)

1. Scenario: operators can inspect the AKS resource types that matter from the MAUI page. Expected result: the WinUI workspace exposes Pods, workloads, batch resources, Helm releases, network resources, ConfigMaps, and Secrets with the expected detail depth.
2. Scenario: diagnostics and current-health surfaces remain readable. Expected result: ingress, network-policy, namespace quota, pod disruption budget, probe-failure, placement, recent events, pod metrics, and HPA context render in the native detail pane without stale results landing after the selected row changes.
3. Scenario: edge-resource shortcuts stay native. Expected result: selected Ingress and HTTPRoute resources expose URL open/copy actions directly from the WinUI detail pane.
4. Scenario: operational actions stay safe under async pressure. Expected result: logs, port-forwarding, shell launch, YAML apply, Helm actions, restart, scale, pod delete, and batch trigger flows do not leave the page in a broken state when navigation changes.

## Automated coverage

- Build validation: `build-winui` must stay green.
- Unit tests: `tests/SwebKit.WinUI.Tests/AksPageViewModelTests.cs` now covers resource-explorer loading across Gateway API, batch, Helm, ConfigMap, and Secret kinds, pod-selection synchronization, preserved diagnostics state while browsing non-pod resources, non-fatal partial-load handling, selected-resource YAML load/apply state, ingress diagnostics state, namespace quota evidence, workload probe-failure evidence, Helm history/values, Helm rollback preview/rollback behavior, CronJob-triggered job refresh behavior, recent events, pod metrics and HPA detail context, selected-resource URL actions, pod delete, and disposal cancellation for selected-resource diagnostics plus restart actions.
- Regression target: rerun touched domain tests if cluster service behavior changes.

## Test data and setup

- Demo mode can validate layout and state behavior.
- Live validation needs a representative AKS context with namespaces and resources that exercise diagnostics and actions.

## Manual checks

- Check: explorer parity. Steps: browse an AKS workspace with real resources and verify Pods, Deployments, StatefulSets, Jobs, CronJobs, Helm releases, Services, ConfigMaps, Secrets, Ingresses, GatewayClasses, Gateways, and HTTPRoutes render in the native explorer with the expected detail pane summary.
- Check: content-first layout parity. Steps: open the native AKS page at a normal desktop size and confirm the explorer, list/detail workspace, and selected-resource facts remain above the fold instead of being pushed down by connection/status chrome.
- Check: YAML, Helm, diagnostics, and event parity. Steps: load YAML for a deployment, ingress, gateway, CronJob, and Helm release; verify edit/apply behavior on supported kinds; inspect ConfigMap and Secret rows; open ingress, network analysis, namespace quotas, pod disruption budgets, probe failures, placement, Helm history, Helm values, upgrade preview, rollback preview, and recent events from the native detail pane and confirm evidence renders without leaving the page.
- Check: edge shortcuts and destructive actions. Steps: open an Ingress and HTTPRoute in the native detail pane, verify URL open/copy actions use the expected host, then delete a pod from the selected-resource action rail and confirm the resource list refreshes cleanly.
- Check: async safety. Steps: start selected-resource diagnostics, YAML, Helm values/history/preview, or a workload action, switch to another resource, then navigate away, and confirm the page disposes cleanly without stale error or success notifications surfacing afterward.

## Regression risks & mitigations

- Risk: new diagnostics views reintroduce duplicated XAML. Mitigation: require shared-primitives adoption before the feature closes.
- Risk: selected-resource async actions resume after disposal and mutate page state after navigation-away. Mitigation: bind selected-resource YAML, diagnostics, and mutation flows to the page lifetime token and keep focused disposal tests in the AKS view-model suite.

## Acceptance criteria

- AKS exposes the agreed resource, YAML, diagnostics, recent-event, and action parity surface that the current MAUI page depends on.
- Shared state and card primitives are used instead of new bespoke layouts.
- `build-winui` stays green and focused AKS state tests exist.
- Remaining live-cluster validation stays explicit as follow-up instead of being implied by the focused automated pass.

## Validation status

- Automated: focused `AksPageViewModelTests` passed `18/18`, `build-winui` is green, and the touched AKS XAML/layout pass validates cleanly in `get_errors`.
- Manual: Live-cluster review still needs to exercise the shipped native baseline and the recent event / URL action / pod delete flows before this feature can move back to `Done`.

## Sign-off

- **Approved by:**
- **Date:** 2026-04-26
- **Conditions (if any):** Keep the feature active until the remaining WinUI parity gaps are closed and the live-cluster AKS review confirms the shipped native baseline.
