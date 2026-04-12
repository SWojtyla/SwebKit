# Test Plan - service-bus-operator-workbench

---

title: "Test Plan - service-bus-operator-workbench"
owner: "GitHub Copilot"
status: "Not started"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Validate that the Service Bus page can support deeper operator triage and preview-first batch workflows without breaking current browse, compose, DLQ, and scheduled-message behavior.

## Scope

- In scope: richer DLQ metadata display, session and partition visibility, explicit message-trace pivots, batch send, batch replay preview and execution summaries, and optional handoff into Incident Timeline or Observability.
- Out of scope: broker provisioning, automatic replay, long-running consumers, and schema-aware business payload tooling.

## Main scenarios (priority)

1. Scenario: inspect a DLQ message that includes dead-letter reason and error description. Expected result: list and detail panes surface the fields consistently and clearly.
2. Scenario: inspect a message with expiry and partition metadata. Expected result: the detail view shows expiry, age, and partition context without requiring raw-property inspection.
3. Scenario: inspect a sessionized workload. Expected result: the page surfaces session grouping and message counts without creating a hidden background consumer.
4. Scenario: inspect a message with explicit trace keys such as `CorrelationId` or `operation_Id`. Expected result: the trace panel shows bounded pivots and explains which identifier was used.
5. Scenario: launch an investigation from a trace pivot. Expected result: downstream handoff into Incident Timeline or Observability carries explicit source context and does not broaden the scope silently.
6. Scenario: batch replay a filtered DLQ selection with remap rules. Expected result: preview shows target entity, message count, remap summary, and environment-aware confirmation before execution.
7. Scenario: batch send a JSON import or template-derived set. Expected result: invalid payloads fail validation before send; successful sends return a clear execution summary.
8. Scenario: one or more replayed messages fail while others succeed. Expected result: the execution summary reports partial success instead of collapsing into a generic failure.
9. Scenario: scoped connection string or missing claims limit available metadata. Expected result: the page surfaces the limitation and keeps unaffected views usable.
10. Scenario: large queue or subscription window. Expected result: bounded loading remains responsive and does not create stale UI state or duplicate actions.

## Automated coverage

- Component tests: `tests/SwebKit.App.Tests`
- Extend `ServiceBusPageTests`, `MessageListViewTests`, `MessageComposerTests`, `ServiceBusNamespacePanelTests`, and `TemplatePickerTests` as needed.
- Add focused coverage for new session or trace panels and batch preview dialogs.
- Unit tests: `tests/SwebKit.Core.Tests`
- Add tests for trace-key extraction, replay preview normalization, JSON import parsing, and any new operator-summary helpers.
- Integration tests: `tests/SwebKit.Azure.Tests`
- Extend `AzureServiceBusClientParsingTests`, `DeadLetterSequenceProcessorTests`, and add session or metadata retrieval tests where new SDK behavior is introduced.
- End-to-end tests: `tests/SwebKit.E2E.Tests`
- Add a focused smoke path for deep triage on `/service-bus` once the new UI is stable.

## Test data and setup

- DLQ fixtures with different reason and error-description combinations.
- Messages carrying `CorrelationId`, `SessionId`, `PartitionKey`, `operation_Id`, and mixed application-property shapes.
- JSON batch send fixtures covering valid arrays, invalid message shapes, oversized payloads, and duplicate IDs.
- Replay fixtures covering full success, partial success, and target-entity validation failures.

## Manual checks

- Check: DLQ metadata readability. Steps: open a dead-lettered message and verify reason, description, expiry, and delivery metadata are visible without raw JSON inspection.
- Check: session visibility. Steps: open a sessionized entity and verify session grouping is explicit and bounded.
- Check: trace pivot safety. Steps: open a message with correlation identifiers and verify the trace panel explains why the pivot exists and where the operator can go next.
- Check: batch replay confirmation. Steps: select multiple DLQ messages, preview replay with remap rules, and verify production confirmation copy and execution summaries.
- Check: batch send validation. Steps: import a valid and invalid payload set and verify invalid items fail before send.

## Regression risks & mitigations

- Risk: new columns and panels make the existing layout unstable. Mitigation: extend bUnit coverage for list density, panel toggles, and narrow-width layouts.
- Risk: batch replay bypasses current production safety. Mitigation: reuse existing confirmation patterns and add focused manual checks on production-like profiles.
- Risk: session inspection causes performance regressions. Mitigation: bound session queries and test large-window behavior with deterministic fixtures.
- Risk: trace pivots imply certainty. Mitigation: render explicit pivot explanations and avoid fuzzy joins.

## Acceptance criteria

- Operators can inspect DLQ, expiry, session, and partition details without resorting to raw-property spelunking.
- Trace pivots remain explicit and bounded.
- Batch replay and send flows are preview-first and report partial success accurately.
- Existing Service Bus browse and mutation flows remain stable.
- Docs and tests evolve with the implementation.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Approved by:
- Date:
- Conditions (if any):
