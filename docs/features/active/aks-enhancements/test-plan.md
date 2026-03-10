# Test Plan - AKS Enhancements

---

title: "Test Plan - AKS Enhancements"
owner: ""
status: "Planned"
created: "2026-03-10"
updated: "2026-03-10"

---

## Goal

Validate end-to-end AKS browsing workflows: kubeconfig context discovery, namespace selection, resource listing, and read-only YAML inspection.

## Scope

- In scope: context discovery, namespace/resource listing, YAML retrieval/display, and auth/RBAC error handling
- Out of scope: resource mutation and management-plane AKS operations

## Main scenarios (priority)

1. Scenario: Load kubeconfig and discover contexts — Expected result: contexts are listed and selectable.
2. Scenario: Select context and namespace — Expected result: all resource tabs scope to selected namespace.
3. Scenario: Open pods/deployments/helm releases/ingresses tabs — Expected result: tab data loads with correct metadata and empty/error states.
4. Scenario: Open YAML for supported resources — Expected result: read-only YAML is shown with kind/name/namespace identity.
5. Scenario: RBAC denied on one resource type — Expected result: tab shows actionable error while other tabs remain usable.

## Automated coverage

- Unit tests: kubeconfig context parsing helpers, namespace/resource mappers, YAML fetch wrappers
- Integration tests: AKS client interactions for list/get by namespace using mocked Kubernetes responses
- UI/component tests: selector state propagation and per-tab loading/error behavior

## Test data and setup

- Fixtures: kubeconfig files (single/multi-context), namespace/resource payloads, YAML samples
- Optional env vars: fixture override path for local runs
- Mocking: mock Kubernetes API where live cluster access is unavailable

## Manual checks

- Check: context dropdown refresh — steps: switch kubeconfig path and verify context list updates.
- Check: namespace propagation — steps: switch namespace and verify all tabs use new namespace.
- Check: YAML viewer behavior — steps: open large manifests and verify read-only usability and scroll performance.

## Regression risks & mitigations

- Risk: stale context/namespace state across reconnects — Mitigation: explicit state sync tests and reconnect smoke checks.
- Risk: large resource collections degrade UI responsiveness — Mitigation: lazy load and targeted refresh operations.

## Acceptance criteria

- Priority scenarios pass
- No regressions in AKS connectivity/auth behavior delivered in archived foundation phase
- Docs and tests reflect implementation reality

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Owner:
- Date:
