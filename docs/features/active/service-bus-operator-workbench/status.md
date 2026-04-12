# Status - service-bus-operator-workbench

---

title: "Status - service-bus-operator-workbench"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-12"

---

## Quick summary

The feature is planned as an additive deepening of the current Service Bus page. The next step is to lock the session and trace contracts before designing the batch replay and send surfaces.

Jira: not linked

Current focus: Wave 1 triage depth for DLQ metadata, session visibility, and explicit trace pivots.

## Progress checklist

### Wave 1 - triage depth

- [ ] Define the list and detail metadata to surface from existing `SbMessage` and `SbSystemProperties`
- [ ] Define any additive session or trace contracts needed in `ServiceBusModels.cs`
- [ ] Define handoff semantics into Incident Timeline or Observability

### Wave 2 - preview-first batch operations

- [ ] Define batch replay preview and execution summary contracts
- [ ] Define batch send import format and validation rules
- [ ] Define environment-aware confirmation rules for destructive actions

### Wave 3 - performance and polish

- [ ] Define bounded large-volume loading rules for sessions and trace pivots
- [ ] Decide whether saved trace pivots or bookmarks are worth shipping in this feature

## Completed

- Confirmed that the current Service Bus page already exposes enough base operations to support an additive workbench plan.
- Confirmed existing model support for `DeadLetterReason`, `DeadLetterErrorDescription`, `SessionId`, and `PartitionKey`, reducing the amount of backend invention required.
- Scoped operator work toward triage clarity and preview-first batch workflows instead of broader queue management.

## Remaining

- Write the detailed session, trace, and batch-operation contracts.
- Align new triage flows with production-safe confirmations and existing environment state.
- Define the validation matrix for large queue windows, scoped credentials, and downstream investigation handoffs.

## Blockers

- None.
- Jira is not linked. Informational only.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- The page must remain explicit about what was observed from the broker versus what is only an operator pivot or suggestion.
- Batch operations should remain opt-in and bounded even when the selected message set is large.
