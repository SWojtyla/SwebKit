# Test Plan - guided-kql-builder

---

title: "Test Plan - guided-kql-builder"
owner: ""
status: "Not started"
created: "2026-03-28"
updated: "2026-03-28"

---

## Goal

Validate that non-KQL users can create useful Logs queries through guided controls, that advanced KQL fallback remains reliable for experts, and that existing Observability query workflows do not regress.

## Scope

- In scope:
  - Guided builder composition in Observability Logs.
  - Query compile correctness and validation behavior.
  - Guided and Advanced mode transitions.
  - Existing query execution compatibility through `IObservabilityProvider`.
  - Persistence behavior for mode and builder draft state.
- Out of scope:
  - Generic KQL parser for arbitrary free-text to builder controls.
  - Cross-resource query execution.
  - Performance testing against very large production Log Analytics datasets.

## Main scenarios (priority)

1. Scenario: Build a basic query (table + time range + row limit) in guided mode - Expected result: valid KQL is generated and returns rows without manual editing.
2. Scenario: Add multiple filters and sort order - Expected result: compiled KQL includes deterministic where/order clauses in expected order.
3. Scenario: Enter invalid guided combination (for example unsupported operator for a column type) - Expected result: validation blocks execution with actionable field-level guidance.
4. Scenario: Switch from guided to advanced mode - Expected result: compiled KQL is transferred intact and editable.
5. Scenario: Edit KQL in advanced mode and switch back - Expected result: system preserves advanced text or prompts when a safe reverse mapping is not available.
6. Scenario: Execute query cancellation (tab switch or explicit cancel) - Expected result: cancellation propagates cleanly and no stale completion state overwrites newer results.
7. Scenario: Persist mode and draft per selected resource/profile - Expected result: reopening Observability restores expected builder/editor state.
8. Scenario: Existing saved query execution in advanced mode - Expected result: continues to work unchanged.
9. Scenario: Demo mode with guided builder - Expected result: behavior remains functional using demo provider data.
10. Scenario: Keyboard-only operation in builder controls and mode toggle - Expected result: all primary actions are accessible without mouse.

## Automated coverage

- Unit tests: `tests/SwebKit.Core.Tests` and `tests/SwebKit.Observability.Tests`
  - Target coverage: >= 80% on new compiler and validation logic.
  - Focus: clause generation, operator/type rules, fallback and cancellation behavior.
- Component tests: `tests/SwebKit.App.Tests`
  - Focus: guided control rendering, mode transitions, validation messages, loading/error/empty states.
- Integration tests: `tests/SwebKit.Observability.Tests`
  - Subsystems under test: compile-plus-execute pipeline from guided definition to provider query call.
  - CI gate: all new and impacted tests pass.
- End-to-end tests: `tests/SwebKit.E2E.Tests`
  - Smoke: create guided query and run.
  - Regression: switch to advanced mode, edit query, rerun, and keep results stable.

## Test data and setup

- Add deterministic in-memory fixtures for common App Insights tables (`traces`, `requests`, `exceptions`).
- Use fake/frozen time provider to avoid flaky relative-time assertions.
- Use mock/fake provider responses for query success, empty result, timeout, and service error.
- Include cancellation token tests to ensure no swallowed cancellation paths.

## Manual checks

- Check: first-time user path in Logs tab
  - Steps: select a resource, build query with guided controls, run, inspect result table.
- Check: advanced user fallback path
  - Steps: switch to advanced mode, edit KQL text, run query, confirm no forced guided parsing.
- Check: mode persistence and draft restoration
  - Steps: configure builder, navigate away, reopen Observability, confirm state restoration.
- Check: accessibility basics
  - Steps: tab through controls, use keyboard to add/remove filters, verify visible focus and labels.

## Regression risks and mitigations

- Risk: Logs tab existing KQL editor behavior regresses.
  - Mitigation: preserve existing advanced execution path and add explicit regression scenarios.
- Risk: Blazor rerender loops trigger duplicate query runs.
  - Mitigation: add guard assertions and component tests based on `OnParametersSetAsync` pitfalls.
- Risk: cancellation is treated as generic failure and pollutes error UX.
  - Mitigation: explicit cancellation tests and separate cancellation handling path.

## Acceptance criteria

- High-priority guided and advanced scenarios pass in CI.
- No regression in existing Observability Logs manual KQL flow.
- New tests for compiler, UI mode transitions, and compile-to-execute integration are present and passing.
- Feature docs remain aligned with implementation updates.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
