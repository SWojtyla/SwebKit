# Test Plan - winui3-aks-parity

---

title: "Test Plan - winui3-aks-parity"
owner: ""
status: "In Progress"
created: "2026-04-25"
updated: "2026-04-27"

---

## Goal

Validate that the native AKS workspace reaches actual MAUI parity for cluster inspection and day-2 actions while staying stable under asynchronous load, resource switching, navigation changes, and the recent compact-layout parity fixes.

## Scope

- In scope: startup scope restore, searchable namespace selection, partial-warning suppression, compact AKS layout behavior, log workspace ergonomics, row-level actions, broader resource coverage, diagnostics panels, operational actions, recent-event visibility, selected-resource URL actions, the native pod-health monitoring manager, native workload-level aggregated logs, and the WinUI keyboard shortcut layer.
- Out of scope: new AKS management features unrelated to current MAUI behavior.

## Main scenarios (priority)

1. Scenario: saved scope restore works. Expected result: the AKS page opens on the persisted context and namespace from Settings before any manual interaction.
2. Scenario: namespace selection remains searchable. Expected result: operators can type to locate a namespace quickly without losing the existing namespace selection workflow.
3. Scenario: the page keeps a compact content-first structure. Expected result: the explorer and detail pane stay visible without a wasted header stack, oversized inactive cards, or an unnecessary page-level vertical scrollbar.
4. Scenario: non-blocking partial failures stay quiet. Expected result: transient resource-load failures do not dominate the page when usable explorer data already loaded.
5. Scenario: operators can inspect the AKS resource types that matter from the MAUI page. Expected result: the WinUI workspace exposes Pods, workloads, batch resources, Helm releases, network resources, ConfigMaps, and Secrets with the expected detail depth.
6. Scenario: diagnostics and current-health surfaces remain readable. Expected result: ingress, network-policy, namespace quota, pod disruption budget, probe-failure, placement, recent events, pod metrics, and HPA context render in the native detail pane without stale results landing after the selected row changes.
7. Scenario: logs feel like a primary workspace. Expected result: Deployments, StatefulSets, and selected pods open logs in a tall investigation surface with a clear close path.
8. Scenario: row actions are discoverable. Expected result: right-click actions cover the agreed MAUI parity set for the selected resource kind and route into the existing safe command paths.
9. Scenario: operational actions stay safe under async pressure. Expected result: logs, port-forwarding, shell launch, YAML apply, Helm actions, restart, scale, pod delete, and batch trigger flows do not leave the page in a broken state when navigation changes.
10. Scenario: pod-health monitoring parity stays inside the AKS route. Expected result: operators can queue namespaces, start or stop monitoring, and review recent pod alerts from the WinUI AKS page, with demo mode exercising the same workflow and persisted state as the live path.

## Automated coverage

- Build validation: `build-winui` must stay green.
- Unit tests: `tests/SwebKit.WinUI.Tests/AksPageViewModelTests.cs` should cover first-load scope restore, namespace changes, resource-explorer loading across Gateway API, batch, Helm, ConfigMap, and Secret kinds, pod-selection synchronization, preserved diagnostics state while browsing non-pod resources, non-fatal partial-load handling, selected-resource YAML load/apply state, ingress diagnostics state, namespace quota evidence, workload probe-failure evidence, Helm history/values, Helm rollback preview/rollback behavior, CronJob-triggered job refresh behavior, recent events, pod metrics and HPA detail context, row action command routing, native monitor-state hydration, native monitor namespace management, external monitor-state propagation, native workload-log streaming, keyboard shortcut handling, and disposal cancellation for selected-resource diagnostics plus restart actions.
- Regression target: rerun touched domain tests if cluster service behavior changes.

## Test data and setup

- Demo mode can validate layout, monitor-state behavior, and the native AKS pod-health workflow without a live cluster.
- Live validation needs a representative AKS context with namespaces and resources that exercise diagnostics and actions.

## Manual checks

- Check: saved scope restore. Steps: persist a non-default AKS context and namespace in Settings, open the AKS page, and confirm the toolbar selections and loaded scope match without manual reselection.
- Check: namespace search parity. Steps: use a namespace list large enough to require search, type into the namespace selector, and verify the desired namespace can be found and selected quickly.
- Check: explorer parity. Steps: browse an AKS workspace with real resources and verify Pods, Deployments, StatefulSets, Jobs, CronJobs, Helm releases, Services, ConfigMaps, Secrets, Ingresses, GatewayClasses, Gateways, and HTTPRoutes render in the native explorer with the expected detail pane summary.
- Check: content-first layout parity. Steps: open the native AKS page at a normal desktop size and confirm the explorer, list/detail workspace, and selected-resource facts remain above the fold instead of being pushed down by connection/status chrome.
- Check: warning-noise parity. Steps: exercise a cluster or demo path that produces partial resource-load failures, then confirm the page stays quiet when the explorer already has usable data and only blocking failures surface prominently.
- Check: YAML, Helm, diagnostics, and event parity. Steps: load YAML for a deployment, ingress, gateway, CronJob, and Helm release; verify edit/apply behavior on supported kinds; inspect ConfigMap and Secret rows; open ingress, network analysis, namespace quotas, pod disruption budgets, probe failures, placement, Helm history, Helm values, upgrade preview, rollback preview, and recent events from the native detail pane and confirm evidence renders without leaving the page.
- Check: row-action discoverability. Steps: right-click Pods, Deployments, StatefulSets, Jobs, CronJobs, Ingresses, and other relevant rows, verify the action menu exposes the agreed parity set for each kind, and confirm those actions target the clicked row instead of stale selection.
- Check: workload logs and keyboard parity. Steps: select a Deployment and a StatefulSet, open the native workload-log surface, confirm aggregated all-pod logs render in the AKS route in a tall readable pane, then use the hint-bar shortcuts (`l`, `y`, `n`, `r`, `/`, `Esc`) to confirm the same core actions remain available without leaving the keyboard.
- Check: pod log workspace parity. Steps: select a pod, open logs, verify container/range/live/filter controls remain readable, the log body has investigation-grade height, and `Close` cleanly dismisses the panel.
- Check: async safety. Steps: start selected-resource diagnostics, YAML, Helm values/history/preview, or a workload action, switch to another resource, then navigate away, and confirm the page disposes cleanly without stale error or success notifications surfacing afterward.
- Check: native monitor parity in demo mode. Steps: enable demo mode, open the AKS page, add one or more namespaces to the native monitor panel, start monitoring, confirm the dashboard/tray-facing monitor state updates, and verify recent pod alerts appear in the AKS route without leaving the page.
- Check: native monitor parity on a live cluster. Steps: from the AKS page, queue namespaces, start monitoring, provoke or wait for a pod-health alert, and confirm recent alerts, dashboard surfaces, and tray unread state stay aligned.

## Regression risks & mitigations

- Risk: new diagnostics views reintroduce duplicated XAML. Mitigation: require shared-primitives adoption before the feature closes.
- Risk: selected-resource async actions resume after disposal and mutate page state after navigation-away. Mitigation: bind selected-resource YAML, diagnostics, and mutation flows to the page lifetime token and keep focused disposal tests in the AKS view-model suite.

## Acceptance criteria

- AKS exposes the agreed resource, YAML, diagnostics, recent-event, workload-log, shortcut, and action parity surface that the current MAUI page depends on.
- The AKS page restores saved context and namespace on first load.
- Namespace search is available and usable.
- Partial-load warnings do not dominate the page when usable explorer data already loaded.
- Pod and workload log views behave like a primary investigation surface rather than a compressed secondary strip.
- Shared state and card primitives are used instead of new bespoke layouts.
- `build-winui` stays green and focused AKS state tests exist.
- Remaining live-cluster validation stays explicit as follow-up instead of being implied by the focused automated pass.

## Validation status

- Automated: the AKS parity plan now requires focused coverage for startup restore, warning suppression, row-action routing, and the existing view-model behaviors; `build-winui` remains the build gate.
- Manual: live-cluster review still needs to exercise startup restore, namespace search, log ergonomics, row actions, monitor parity, and the existing diagnostics and mutation flows before this feature can move to `Done`.

## Sign-off

- **Approved by:**
- **Date:** 2026-04-26
- **Conditions (if any):** Keep the feature active until the known parity complaints are closed and the live-cluster AKS review confirms the shipped native baseline, including startup restore, namespace search, log workspace quality, row actions, monitor parity, and the native workload-log and shortcut surfaces.
