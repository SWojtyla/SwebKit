# Test Plan - AKS Enhancements

---

title: "Test Plan - AKS Enhancements"
owner: ""
status: "Partially Complete"
created: "2026-03-10"
updated: "2026-03-11"

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

- Unit tests (24 passing in `SwebKit.Kubernetes.Tests`):
  - Auth helpers: server-id extraction from kubeconfig (inline, equals-style, missing, empty), scope construction (GUID, api:// prefix, empty), fallback gating (AKS hosts, non-AKS, with/without token, null/empty)
  - Chart version parsing: standard charts, pre-release versions, no version, empty/null
  - Client configuration: default config behavior, invalid context rejection
  - Constructor: invalid context throws helpful exception
- Integration tests: Deferred (requires live cluster or mocked Kubernetes API)
- UI/component tests: Deferred

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

- Automated: 24/24 unit tests passing
- Manual: Not started

## Sign-off

- Owner:
- Date:
