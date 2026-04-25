# Test Plan - winui3-settings-completeness

---

title: "Test Plan - winui3-settings-completeness"
owner: ""
status: "Review"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Validate that every in-scope operator domain has a native WinUI settings surface for configuration, validation, and repair, and that readiness actions can reliably send operators to the correct section.

## Scope

- In scope: Settings sections for Service Bus, AKS, Redis, DevOps, Storage, and Observability; per-section validation; readiness deep-links
- Out of scope: domain workspace behavior beyond configuration and repair

## Main scenarios (priority)

1. Scenario: each in-scope domain has a native Settings section. Expected result: operators can review and update configuration without reverting to MAUI.
2. Scenario: a route-level readiness issue opens the correct Settings section. Expected result: Pipelines and Observability readiness actions land on the exact repair surface.
3. Scenario: saved settings persist and reload cleanly. Expected result: the WinUI host keeps the same effective configuration after restart.

## Automated coverage

- Build validation: `build-winui` must stay green as Settings XAML and view-model logic expand.
- Unit tests: focused `tests/SwebKit.WinUI.Tests/ReadinessStateViewModelTests.cs` coverage currently verifies page-level settings-request payloads plus request normalization; dashboard deep links, frame handoff, and section-form save flows still rely on build validation plus manual smoke coverage for now.
- Regression target: keep the existing Pipelines/Observability readiness tests green while the Settings repair path moves.

## Test data and setup

- Demo mode covers first-pass shell validation.
- Live validation needs representative config for each domain plus stored credentials where required.

## Manual checks

- Check: section availability. Steps: open Settings and confirm every in-scope domain has a native section with actionable guidance.
- Check: readiness repair loop. Steps: trigger an invalid or missing configuration state, open Settings from the route-level action, repair the config, and retry the route.

## Regression risks & mitigations

- Risk: Settings stores values that downstream routes no longer read. Mitigation: validate the full repair loop from route to Settings and back.
- Risk: the Settings page becomes too broad to navigate. Mitigation: keep section grouping and headers aligned with the layout-redesign contract.

## Acceptance criteria

- Every in-scope domain has a native configuration and repair surface.
- Readiness actions open the right Settings section.
- `build-winui` stays green and focused WinUI settings tests are in place.

## Validation status

- Automated: `build-winui` green and focused `ReadinessStateViewModelTests` command-level settings-navigation coverage passing; no dedicated dashboard/frame-handoff or section-form persistence coverage yet
- Manual: Pending WinUI UI smoke across the new settings sections

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
