# Test Plan - service-bus-sbinspector-parity

---

title: "Test Plan - service-bus-sbinspector-parity"
owner: "Unassigned"
status: "Planned"
created: "2026-03-28"
updated: "2026-03-28"

---

## Goal

Validate that SwebKit Service Bus reaches functional parity with SBInspector for scoped parity items, with emphasis on operational capability while maintaining SwebKit UX consistency, production safety cues, and keyboard/accessibility behavior.

## Scope

- In scope:
  - Wave 1 to Wave 5 capabilities (mandatory parity scope)
  - Filtered export parity for JSON output in the current feature scope
  - Backend contract behavior and Azure SDK edge handling for new operations
  - Blazor Hybrid UI state handling across loading/error/empty/loaded states
  - Regression coverage for existing Service Bus workflows (peek, send, schedule, DLQ actions)
- Out of scope:
  - Non-Service Bus feature areas (AKS, Redis, Storage, Observability)
  - Visual cloning of SBInspector layout
  - Settings/theming parity with SBInspector
  - CSV filtered export (deferred follow-up after parity waves)
  - Performance benchmarking beyond practical paging and UI responsiveness checks

## Main scenarios (priority)

1. Entity enable/disable: Queue, topic, and subscription state toggles succeed when claims allow and show clear errors when claims do not.
2. Single-message delete: User can delete a selected message from active queue and DLQ views with explicit confirmation.
3. Purge-all operation: Purge flow handles empty and large entities, enforces production confirmation, and reports deleted counts.
4. Multi-field filtering: Filters combine fields and operators (equals, contains, starts-with, numeric/date comparisons where applicable).
5. Filter persistence and toggle: Saved filters restore per context and can be temporarily disabled without losing definitions.
6. Delete filtered set: Deleting only matching filtered messages shows preview count and does not affect non-matching rows.
7. Export filtered set (JSON): Exported output contains only filtered messages and includes expected metadata fields.
8. Column customization: User can select built-in columns, add custom-property columns, and persist/reload preferences.
9. Row density persistence: Density choice remains consistent across reopen/reload and does not break keyboard row navigation.
10. Pagination/load-more: Load-more appends additional pages without dropping active filters or selection context.
11. Message templates lifecycle: Create/update/delete/apply template flows work in message composer and survive app restart.
12. Auto-refresh after operations: Lists refresh predictably after send, delete, purge, and template-driven sends.
13. Accessibility and keyboard checks: Core operations remain keyboard reachable with consistent focus behavior.
14. Scoped connection string behavior: Entity-scoped connections reflect scope-limited listings without misleading empty-state semantics.
15. Cancellation behavior: User-triggered cancellation propagates without swallowing `OperationCanceledException`.

## Automated coverage

- Unit tests: `tests/SwebKit.Core.Tests/`, `tests/SwebKit.Azure.Tests/`
  - Target: meaningful coverage of new filtering, template persistence, pagination contracts, and operation safety checks.
- Component tests: `tests/SwebKit.App.Tests/`
  - Target: state transitions, lifecycle guards, and interaction flows for Service Bus components.
- End-to-end smoke/regression:
  - Extend `tests/SwebKit.E2E.Tests/` for at least one end-to-end scenario per wave (W1-W5).

## Test data and setup

- Service Bus test fixtures:
  - Messages with varied body payloads (JSON/text), system properties, and custom application properties.
  - Entities with mixed statuses (enabled/disabled), active and DLQ messages, and scheduled messages.
- Mocking strategy:
  - Use test doubles for `IServiceBusClient` in component tests to drive deterministic UI states.
  - Use focused Azure tests in `SwebKit.Azure.Tests` to verify Azure SDK interaction behavior for new operations.
- Configuration fixtures:
  - Persisted filter profiles, column profiles, density preference, and template definitions with migration-safe defaults.

## Manual checks

- Check: Production safety cues for destructive actions.
  - Steps: switch to production-tier environment, execute delete/purge flows, confirm dialogs require explicit confirmation, verify action only proceeds after confirmation.
- Check: Keyboard-only operation.
  - Steps: navigate entity tree, list actions, filter controls, and composer template flows using keyboard only; verify focus indicators and action execution.
- Check: Error and recovery paths.
  - Steps: simulate insufficient claims, scoped connection limits, and cancellation; verify actionable error text and no stuck loading states.

## Regression risks & mitigations

- Risk: Azure SDK admin/listing auth mismatches create false positive connection states.
  - Mitigation: Validate connection checks against same listing path used in operational flows (AZ-1).
- Risk: Scoped connection behavior appears as missing data.
  - Mitigation: Clearly communicate scope-limited listings and test entity-path-specific cases (AZ-2).
- Risk: Blazor lifecycle regressions (double load, stale state, missing re-render).
  - Mitigation: Apply lifecycle guards and `InvokeAsync(StateHasChanged)` patterns where required (BL-2/BL-3/BL-5).
- Risk: Cancellation and exception handling regressions.
  - Mitigation: Explicit `OperationCanceledException` propagation tests (CS-2).

## Acceptance criteria

- All high-severity parity items are implemented and validated (Waves 1-5).
- Medium-severity items in agreed scope are implemented and validated.
- Filtered export parity in this feature is validated for JSON output; CSV is deferred and not required for this feature sign-off.
- Theming/settings parity is not required for this feature sign-off.
- No critical regression in existing Service Bus workflows.
- Production safety cues and keyboard accessibility remain consistent.
- Feature docs and Service Bus functionality docs are aligned with implemented behavior.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- **Approved by:** Pending
- **Date:** Pending
- **Conditions (if any):** Pending
