# Test Plan - aks

---

title: "Test Plan - aks"
owner: ""
status: "Planned"
created: "2026-03-08"
updated: "2026-03-08"

---

## Goal

Validate that users can load kubeconfig, select namespaces, browse pods/deployments/helm releases/ingresses, and inspect raw YAML safely and reliably.

## Scope

- In scope: kubeconfig load, context switch, namespace selection, resource list views, YAML view retrieval/display, error states
- Out of scope: resource mutation (apply/delete), live tail/port-forward/terminal flows

## Main scenarios (priority)

1. Scenario: Load default and custom kubeconfig files — Expected result: contexts are parsed and selectable.
2. Scenario: Select namespace and open Pods/Deployments/Helm Releases/Ingresses — Expected result: each view shows namespace-scoped data.
3. Scenario: Open YAML for each resource type — Expected result: full YAML is displayed read-only with correct metadata.

## Automated coverage

- Unit tests: kubeconfig parsing, namespace/resource mappers, YAML fetch wrappers — target coverage: core AKS client paths
- Integration tests: Kubernetes client interactions for list/get by namespace — CI gates on AKS client test project
- End-to-end tests: UI journey (`kubeconfig -> namespace -> resource -> yaml`) as smoke suite

## Test data and setup

- Required fixtures: sample kubeconfig files (single-context and multi-context), mocked namespace/resource responses
- Environment vars: optional kubeconfig fixture path for test runs
- Mocking strategy: mock Kubernetes API responses in unit/integration layers where cluster access is unavailable

## Manual checks

- Check: Kubeconfig error handling — steps: load invalid/expired kubeconfig and verify actionable error messaging.
- Check: Namespace scoping — steps: switch namespace and verify all resource tabs refresh consistently.
- Check: YAML viewer usability — steps: open YAML for each resource type and verify read-only behavior and scroll performance.

## Regression risks & mitigations

- Risk: namespace selection not propagated to all resource tabs — Mitigation: add shared state tests and component assertions.
- Risk: large YAML payload rendering issues — Mitigation: lazy load YAML and test with large fixture files.

## Acceptance criteria

- All high-priority scenarios pass in CI
- No critical regressions in AKS page navigation and resource rendering
- Tests and AKS feature docs updated

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Owner:
- Date:
