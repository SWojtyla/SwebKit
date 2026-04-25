# Test Plan - winui3-cutover-audit-hardening

---

title: "Test Plan - winui3-cutover-audit-hardening"
owner: ""
status: "In Progress"
created: "2026-04-24"
updated: "2026-04-25"

---

## Goal

Validate the final WinUI migration cutover once the split dependency features land, and produce a defensible recommendation on whether the MAUI host can move to legacy-only status.

## Scope

- In scope: cross-feature integration, demo-mode smoke, live-environment smoke, readiness verification, and the final cutover recommendation
- In scope: all active WinUI migration features that feed the cutover gate
- Out of scope: the implementation-specific test plans owned by the individual feature folders

## Main scenarios (priority)

1. Scenario: the split feature sequence actually produces one coherent native host. Expected result: layout redesign, settings completeness, and the domain parity slices work together without falling back to MAUI assumptions.
2. Scenario: environment-sensitive routes remain survivable. Expected result: Pipelines and Observability still surface actionable readiness guidance and recover through native Settings.
3. Scenario: the WinUI host is cutover-ready. Expected result: the repo can state, with evidence, whether `SwebKit.App` is still required.

## Automated coverage

- Build validation: `build-winui` must stay green after every dependency feature lands.
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

- Every cutover-critical split feature has an explicit outcome: complete, deferred with justification, or blocked.
- The native-host smoke suite has been executed and recorded.
- The repo has an explicit cutover recommendation backed by validation evidence.

## Validation status

- Automated: last known baseline remains green (`build-winui`, `tests/SwebKit.WinUI.Tests`, `tests/SwebKit.DevOps.Tests`), but the split feature execution has not started yet.
- Manual: Not started

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
