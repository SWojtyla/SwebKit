# Test Plan - winui3-aks-parity

---

title: "Test Plan - winui3-aks-parity"
owner: ""
status: "Not started"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Validate that the native AKS workspace reaches the required MAUI parity for cluster inspection and operational actions while staying stable under asynchronous load and navigation changes.

## Scope

- In scope: broader resource coverage, diagnostics panels, operational actions, shared-card adoption
- Out of scope: new AKS management features unrelated to current MAUI behavior

## Main scenarios (priority)

1. Scenario: operators can inspect the resource types that still matter from the MAUI page. Expected result: the WinUI workspace exposes the agreed resource coverage and detail depth.
2. Scenario: diagnostics panels reflect cluster health clearly. Expected result: health, event, and detail cards use the shared primitives and remain readable under loading, empty, and error states.
3. Scenario: operational actions stay safe under async pressure. Expected result: logs, port-forwarding, shell launch, and other retained actions do not leave the page in a broken state when navigation changes.

## Automated coverage

- Build validation: `build-winui` must stay green.
- Unit tests: expand `tests/SwebKit.WinUI.Tests/` for AKS view-model state transitions, especially around loading, disposal, and readiness.
- Regression target: rerun touched domain tests if cluster service behavior changes.

## Test data and setup

- Demo mode can validate layout and state behavior.
- Live validation needs a representative AKS context with namespaces and resources that exercise diagnostics and actions.

## Manual checks

- Check: diagnostics parity. Steps: browse an AKS workspace with real resources and verify health, events, and detail panes match the planned parity surface.
- Check: async safety. Steps: start an AKS action, navigate away, and confirm the page disposes and recovers cleanly.

## Regression risks & mitigations

- Risk: new diagnostics views reintroduce duplicated XAML. Mitigation: require shared-primitives adoption before the feature closes.
- Risk: async actions resume after disposal. Mitigation: extend focused tests around navigation-away behavior.

## Acceptance criteria

- AKS exposes the agreed resource and diagnostics parity surface.
- Shared state and card primitives are used instead of new bespoke layouts.
- `build-winui` stays green and focused AKS state tests exist.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
