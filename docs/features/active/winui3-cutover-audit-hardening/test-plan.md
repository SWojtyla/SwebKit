# Test Plan - winui3-cutover-audit-hardening

---

title: "Test Plan - winui3-cutover-audit-hardening"
owner: ""
status: "Done"
created: "2026-04-24"
updated: "2026-04-26"

---

## Goal

Record the cutover validation surface for the split WinUI migration wave and preserve the explicit current recommendation when the coordination umbrella is closed before a final native-host cutover gate is executed.

## Scope

- In scope: cross-feature integration, demo-mode smoke, live-environment smoke, readiness verification, and the final cutover recommendation
- In scope: all active WinUI migration features that feed the cutover gate
- Out of scope: the implementation-specific test plans owned by the individual feature folders
- Out of scope for this archived wave: claiming `SwebKit.App` can move to legacy-only status without a later dedicated cutover gate

## Main scenarios (priority)

1. Scenario: the split feature set actually produces one coherent native host. Expected result: the shared layout and settings contracts plus the domain parity slices work together without falling back to MAUI assumptions.
2. Scenario: environment-sensitive routes remain survivable. Expected result: Pipelines and Observability still surface actionable readiness guidance and recover through native Settings.
3. Scenario: the WinUI host is cutover-ready. Expected result: the repo can state, with evidence, whether `SwebKit.App` is still required.

## Automated coverage

- Build validation: `build-winui` must stay green after every coordinated feature lands.
- Feature validation: each split feature must complete its own focused automated checks before it can be counted toward the cutover gate.
- Cross-feature baseline: keep the existing WinUI and DevOps test projects green while the split features land.

## Test data and setup

- Demo mode remains the baseline smoke path for shell, layout, and route activation validation.
- Live validation still needs representative Service Bus, AKS, Redis, Storage, DevOps, and Observability config in `%APPDATA%/SwebKit` plus matching credentials in Windows Credential Manager and Azure login state.

## Manual checks

- Check: full native route walkthrough. Steps: launch the WinUI app and visit Dashboard, Settings, Service Bus, AKS, Redis, Storage, Pipelines, and Observability after the split features land.
- Check: readiness repair loop. Steps: validate Pipelines and Observability under both demo mode and a live-configured machine; confirm the route-level readiness actions recover through native Settings.
- Check: cutover gate review. Steps: compare the remaining debt list against the split feature outcomes and record the go or no-go recommendation.

## Regression risks & mitigations

- Risk: split feature completion is mistaken for cutover readiness without integration testing. Mitigation: require the full route walkthrough and explicit recommendation in this umbrella.
- Risk: environment-specific failures are mistaken for host instability. Mitigation: keep live validation notes separate from demo-mode structural checks.

## Acceptance criteria

- Every cutover-critical split feature has an explicit archived outcome or future-follow-up note.
- If the native-host smoke suite is not executed before the umbrella closes, the repo records that fact explicitly instead of implying cutover readiness.
- The repo has an explicit current recommendation backed by available evidence, even when that recommendation is "not ready to retire `SwebKit.App` from this archived wave."

## Validation status

- Automated: last known baseline remained green (`build-winui`, `tests/SwebKit.WinUI.Tests`, `tests/SwebKit.DevOps.Tests`) at the time this coordination wave was archived.
- Manual: the final native-host smoke suite was not executed before this umbrella closed.

## Sign-off

- **Approved by:**
- **Date:** 2026-04-26
- **Conditions (if any):** This archived wave does not claim cutover readiness; future one-by-one follow-up must reopen dedicated slices and a later cutover gate if retirement of `SwebKit.App` is still desired.
