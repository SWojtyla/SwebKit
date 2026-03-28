# Test Plan - <feature-name>

---

title: "Test Plan - <feature-name>"
owner: ""
status: "Not started"
created: ""
updated: ""

---

## Goal

Concise statement of what testing must validate (behaviour, performance, compatibility).

## Scope

- In scope: critical flows, supported platforms, integrations
- Out of scope: exploratory testing, non-goal components

## Main scenarios (priority)

1. Scenario: [brief description] — Expected result
2. Scenario: [brief description] — Expected result
3. Scenario: [brief description] — Expected result

## Automated coverage

- Unit tests: `<project>.Tests` — target coverage: _e.g., 80% on new code_
- Integration tests: _describe subsystems under test_ — CI gates: _pass/fail threshold_
- End-to-end tests: _describe user journeys_ — smoke + regression suites

## Test data and setup

- Required fixtures, seeds, and environment variables
- Mocking strategy for external services

## Manual checks

- Check: [what to validate] — steps
- Check: [what to validate] — steps

## Regression risks & mitigations

- Risk: [risk description] — Mitigation: [action]
- Risk: [risk description] — Mitigation: [action]

## Acceptance criteria

- All high-priority scenarios pass in CI
- No critical regressions
- Tests and docs updated

## Validation status

- Automated: Not started / In progress / Passed
- Manual: Not started / In progress / Passed

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
