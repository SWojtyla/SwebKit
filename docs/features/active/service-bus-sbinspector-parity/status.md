# Status - service-bus-sbinspector-parity

---

title: "Status - service-bus-sbinspector-parity"
owner: "Unassigned"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-03-28"
last_updated: "2026-03-28"

---

## Quick summary

Feature planning is complete and structured into five delivery waves focused on functional parity and operational capability; next step is implementation kickoff for Wave 1 backend and UI contracts.

**Jira:** not linked

**Current focus:** Finalize acceptance details for Wave 1 critical entity/message management and JSON-first filtered export scope, then begin implementation execution.

## Progress checklist

### Wave 0 - Planning and parity baseline

- [x] Create active feature folder and core planning docs
- [x] Capture parity scope, assumptions, constraints, and wave sequence
- [x] Define test strategy and risk controls
- [ ] Confirm stakeholder acceptance criteria for each wave

### Wave 1 - Critical entity and message management

- [ ] Add queue/topic/subscription enable/disable support
- [ ] Add single-message delete from message list and DLQ contexts
- [ ] Add purge-all workflow with production safety confirmations
- [ ] Add auto-refresh after mutative operations in this wave
- [ ] Add or update backend/unit/component tests for critical operations

### Wave 2 - Advanced filtering and filtered operations

- [ ] Add multi-field filters with explicit operators and logical composition
- [ ] Add filter persistence and filter on/off toggle behavior
- [ ] Add delete filtered messages flow with preview and confirmation
- [ ] Add export filtered messages flow (JSON only for parity wave; CSV deferred follow-up)
- [ ] Add tests for filtering logic, persistence, and filtered actions

### Wave 3 - Column customization and density

- [ ] Add column chooser for built-in fields
- [ ] Add custom-property columns for message application properties
- [ ] Persist per-view column profiles and row density preferences
- [ ] Keep keyboard navigation and accessibility consistent after customization
- [ ] Add component tests for column profile/state persistence

### Wave 4 - Pagination and load-more

- [ ] Add load-more paging behavior for large message sets
- [ ] Preserve filter/sort/selection semantics across pages
- [ ] Ensure paging interactions are responsive in Blazor Hybrid
- [ ] Add tests for paging continuation and regression scenarios

### Wave 5 - Message templates

- [ ] Add template create/save/update/delete flows in message composer
- [ ] Add template apply flow for queue/topic send scenarios
- [ ] Persist templates with clear environment or namespace scope rules
- [ ] Add tests for template lifecycle and invalid-template handling

## Completed

- Active feature folder created with required planning documents.
- Severity-based parity gaps mapped to implementation waves.
- Architecture, pitfall, and documentation coupling constraints captured.
- Scope decisions captured: no theming/settings parity in this feature, and filtered export is JSON-first with CSV deferred.

## Remaining

- All implementation waves (1 through 5).
- Validation execution and result tracking once implementation starts.
- Final readiness review before moving state to `Review`.
- Deferred follow-up tracking for CSV export and any theming/settings parity requests.

## Blockers

- No blocker currently.
- Scope alignment is resolved in planning docs for current feature boundaries.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Assumptions

- No Jira ticket link is available at planning time.
- High- and medium-severity parity items are mandatory for this feature's success criteria.
- Theming/settings parity is explicitly out of scope for this feature.
- Filtered export parity in this feature is JSON-first; CSV export is deferred follow-up work.

## Notes

- This feature must preserve SwebKit UX consistency and safety-first production behaviors while adding SBInspector-level capabilities.
- Any implementation that changes Service Bus behavior must update `docs/architecture/functionalities/service-bus.md` in the same change set.
