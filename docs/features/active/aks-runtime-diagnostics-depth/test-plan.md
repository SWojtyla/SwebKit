# Test Plan - aks-runtime-diagnostics-depth

---

title: "Test Plan - aks-runtime-diagnostics-depth"
owner: "GitHub Copilot"
status: "Not started"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Validate that the AKS page can surface deeper runtime diagnostics and read-only change previews while staying responsive, evidence-based, and compatible with existing resource, logs, YAML, and Helm flows.

## Scope

- In scope: namespace quota and limit visibility, PodDisruptionBudget status, probe-failure surfacing, placement analysis, network policy and ingress analysis, Helm diff or upgrade preview.
- Also in scope for current AKS page hardening: network resource navigation, Services browse support, and stable HTTPRoute list rendering.
- Out of scope: mutating Kubernetes policy objects, packet tracing, full scheduler simulation, and automatic repair.

## Main scenarios (priority)

1. Scenario: selected namespace is near or over quota. Expected result: quota panel surfaces the constrained resource and current usage clearly.
2. Scenario: a workload is constrained by `LimitRange` defaults or limits. Expected result: the relevant limits appear in the namespace diagnostics surface.
3. Scenario: a PodDisruptionBudget blocks an eviction or rollout. Expected result: the selected workload shows PDB status and current allowed disruptions.
4. Scenario: readiness or liveness probes are failing repeatedly. Expected result: the probe panel summarizes the failing containers and recent event evidence.
5. Scenario: a pod is unschedulable due to node selectors, affinity, topology spread, or taints. Expected result: the placement panel shows the active constraints and recent scheduling failure events.
6. Scenario: ingress points to a backend with missing endpoints or mismatched rules. Expected result: ingress analysis surfaces the mapping and the visible gap.
7. Scenario: network policy likely restricts the selected workload. Expected result: the panel shows the relevant policies and explains what is known versus unknown.
8. Scenario: Helm diff preview is requested when supporting CLI tooling is available. Expected result: a read-only preview renders and remains searchable.
9. Scenario: Helm diff preview is requested when supporting tooling is unavailable. Expected result: the page shows an explicit capability limitation or degraded fallback.
10. Scenario: multiple diagnostics panels are opened while auto-refresh would normally run. Expected result: the main grid remains stable and auto-refresh pause behavior still works.
11. Scenario: the operator opens the AKS `Network` menu. Expected result: Services, Ingresses, GatewayClasses, Gateways, and HTTPRoutes are all reachable without flattening the main toolbar.
12. Scenario: all-namespaces mode contains Services from more than one namespace. Expected result: the Services tab shows row namespace, service type, and route-to-YAML behavior against the selected row namespace.
13. Scenario: three or more HTTPRoutes are present and route cells wrap. Expected result: every route remains visible in the UI and later rows are still reachable.

## Automated coverage

- Component tests: `tests/SwebKit.App.Tests`
- Extend `AksDetailPanelsTests`, `AksHelmPanelTests`, `AksPageBatchTests`, `AksConnectionBarTests`, `AksYamlViewerTests`, and add focused tests for new diagnostics panels.
- Unit tests: `tests/SwebKit.Core.Tests`
- Add model and summary tests if placement or probe summarization logic lives in Core helpers.
- Integration tests: `tests/SwebKit.Kubernetes.Tests`
- Extend `KubernetesAksClientTests` for quotas, PDBs, probe data extraction, ingress analysis, network policy reads, and Helm preview capability detection.
- Demo-mode coverage: `tests/SwebKit.Core.Tests/DemoAksClientTests.cs`
- Extend demo client fixtures if the new diagnostics are exposed in demo mode.

## Test data and setup

- Namespace fixtures with quotas and limit ranges.
- Workload fixtures with matching and non-matching PodDisruptionBudgets.
- Pod fixtures with repeated readiness and liveness probe failures and FailedScheduling events.
- Ingress and Service fixtures covering valid routing, missing endpoints, and host or TLS mismatches.
- Helm preview fixtures for both plugin-present and plugin-missing environments.

## Manual checks

- Check: namespace diagnostics. Steps: open `/aks`, select a constrained namespace, and verify quota and limit details are visible and understandable.
- Check: probe and placement explanation. Steps: inspect a failing pod and verify the panel distinguishes observed failures from inferred next steps.
- Check: network and ingress analysis. Steps: select an ingress or workload with policy restrictions and confirm the page shows what objects were inspected.
- Check: network menu browse. Steps: open the AKS `Network` menu, switch between Services, Ingresses, and HTTPRoutes, and confirm the active resource list and count update correctly.
- Check: Helm preview. Steps: request a preview and verify plugin-supported and unsupported paths are both explicit.

## Regression risks & mitigations

- Risk: new diagnostics panels destabilize the current detail-panel layout. Mitigation: extend layout-focused component tests and keep the single panel column model.
- Risk: large namespace queries slow the page. Mitigation: add bounded query patterns and focus tests on large fixtures.
- Risk: users read network or placement summaries as definitive root cause. Mitigation: copy review and tests for explanation wording.

## Acceptance criteria

- Operators can inspect quota, disruption, probe, placement, network, and Helm preview context without leaving the app for basic evidence gathering.
- The UI remains explicit about what it knows and what it cannot prove.
- Existing AKS flows remain stable.
- Test coverage and docs are updated with the implementation.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Approved by:
- Date:
- Conditions (if any):
