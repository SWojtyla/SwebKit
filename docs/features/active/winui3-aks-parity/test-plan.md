# Test Plan - winui3-aks-parity

---

title: "Test Plan - winui3-aks-parity"
owner: ""
status: "In Progress"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Validate that the native AKS workspace reaches the required MAUI parity for cluster inspection and operational actions while staying stable under asynchronous load and navigation changes.

## Scope

- In scope: broader resource coverage, diagnostics panels, operational actions, shared-card adoption
- Out of scope: new AKS management features unrelated to current MAUI behavior

## Main scenarios (priority)

1. Scenario: operators can inspect the resource types that still matter from the MAUI page. Expected result: the WinUI workspace exposes Pods, workloads, batch resources, and Gateway API resources with the expected detail depth.
2. Scenario: diagnostics panels reflect cluster health clearly. Expected result: ingress and network-policy evidence load into the native detail pane and remain readable under loading, empty, and error states.
3. Scenario: operational actions stay safe under async pressure. Expected result: logs, port-forwarding, shell launch, YAML apply, restart, scale, and batch trigger flows do not leave the page in a broken state when navigation changes.

## Automated coverage

- Build validation: `build-winui` must stay green.
- Unit tests: `tests/SwebKit.WinUI.Tests/AksPageViewModelTests.cs` now covers resource-explorer loading across Gateway API and batch kinds, pod-selection synchronization, preserved diagnostics state while browsing non-pod resources, non-fatal partial-load handling, selected-resource YAML load/apply state, ingress diagnostics state, CronJob-triggered job refresh behavior, and disposal cancellation for selected-resource diagnostics plus restart actions.
- Regression target: rerun touched domain tests if cluster service behavior changes.

## Test data and setup

- Demo mode can validate layout and state behavior.
- Live validation needs a representative AKS context with namespaces and resources that exercise diagnostics and actions.

## Manual checks

- Check: explorer parity. Steps: browse an AKS workspace with real resources and verify Pods, Deployments, StatefulSets, Jobs, CronJobs, Services, Ingresses, GatewayClasses, Gateways, and HTTPRoutes render in the native explorer with the expected detail pane summary.
- Check: YAML and diagnostics parity. Steps: load YAML for a deployment, ingress, gateway, and CronJob; verify edit/apply behavior on supported kinds; open ingress and network analysis from the native detail pane and confirm evidence renders without leaving the page.
- Check: async safety. Steps: start selected-resource diagnostics, YAML, or a workload action, navigate away, and confirm the page disposes cleanly without stale error or success notifications surfacing afterward.

## Regression risks & mitigations

- Risk: new diagnostics views reintroduce duplicated XAML. Mitigation: require shared-primitives adoption before the feature closes.
- Risk: selected-resource async actions resume after disposal and mutate page state after navigation-away. Mitigation: bind selected-resource YAML, diagnostics, and mutation flows to the page lifetime token and keep focused disposal tests in the AKS view-model suite.

## Acceptance criteria

- AKS exposes the agreed resource, YAML, diagnostics, and first-action parity surface.
- Shared state and card primitives are used instead of new bespoke layouts.
- `build-winui` stays green and focused AKS state tests exist.

## Validation status

- Automated: focused `dotnet test .\tests\SwebKit.WinUI.Tests\SwebKit.WinUI.Tests.csproj -c Release --filter "FullyQualifiedName~AksPageViewModelTests"` passed with 9 tests.
- Manual: Not started

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
