# Status - service-bus-operator-workbench

---

title: "Status - service-bus-operator-workbench"
owner: "GitHub Copilot"
state: "In Progress"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-05-01"

---

## Quick summary

Wave 1 (triage depth) is complete. Wave 2 (preview-first batch operations) is complete. The Service Bus DLQ now has a `BatchReplayPanel` with target-entity override, remap rules (subject, correlationId, property renames/removes), preview → confirm → execute → summary flow. `BatchSendPanel` accepts a JSON array, validates entries, and sends with a per-batch execution summary. `ServiceBusPage` exposes a "Batch Send" toolbar button for regular entities. 27 new Wave 2 tests (15 BatchReplayPanel + 12 BatchSendPanel) all passing.

Jira: not linked

Current focus: Wave 3 polish.

## Progress checklist

### Wave 1 - triage depth ✅

- [x] Enriched System tab: `PartitionKey`, `ExpiresAt` with TTL-remaining cue, `LockedUntil`
- [x] DLQ Info tab: delivery count and expiry in meta block alongside reason and description
- [x] Trace Pivot tab in `MessageDetailPane`: explicit pivots from `CorrelationId`, `MessageId`, `SessionId`, and known app-property keys (`operation_Id`, `traceparent`, etc.) with per-pivot reason text
- [x] Investigate button per pivot — launches Incident Timeline via `IncidentInvestigationLauncher` with scoped `IncidentSeedEvidenceRef`
- [x] Session quick-filter CTA in System tab: "Filter list to this session" button triggers `OnFilterBySession` EventCallback
- [x] `PinnedSessionId` parameter on `MessageListView`: applies bounded session filter with visible badge
- [x] `PartitionKey` column in `MessageListView` column chooser (off by default)
- [x] `ServiceBusPage` wired: `PinSessionFilter` handler and both detail pane usages updated
- [x] 18 tests in `MessageDetailPaneTests` + 3 tests in `MessageListViewTests`

### Wave 2 - preview-first batch operations ✅

- [x] `BatchOperationResult` + `BatchOperationItemError` + `BatchSendEntry` models in `ServiceBusModels.cs`
- [x] `IServiceBusClient.ResubmitDeadLetterAsync` updated to accept `RemapRules? remapRules = null`
- [x] `AzureServiceBusClient.ResubmitDeadLetterAsync` applies remap rules (subject, correlationId, property renames, property removes) via `ApplyRemapRules` helper
- [x] `DemoServiceBusClient` and all test `FakeServiceBusClient` stubs updated to new signature
- [x] `BatchReplayPanel.razor`: config step (target entity, namespace selector, remap rules as collapsible details) → confirm step (count/source/target summary, production warning) → execute (chunked, progress indicator) → summary (`BatchOperationResult`)
- [x] `BatchSendPanel.razor`: JSON import → validate (`body` required, auto-assign `messageId`) → preview table (valid/invalid counts, per-row status) → execute (chunked `SendBatchAsync`) → summary
- [x] `DlqView.razor`: "Resubmit N…" button opens inline `BatchReplayPanel`; `AvailableNamespaces` parameter threaded through from `ServiceBusPage`
- [x] `ServiceBusPage.razor`: `BatchSendPanel` in `Modal`, "Batch Send" button in `RoutePageHeader` Actions slot (shown only for non-DLQ, non-scheduled tabs)
- [x] 15 tests in `BatchReplayPanelTests` + 12 tests in `BatchSendPanelTests` (27 total, all passing)

### Wave 3 - performance and polish

- [ ] Bounded large-volume loading rules for sessions and trace pivots
- [ ] Saved trace pivots or bookmarks

## Completed

- Wave 1: enriched detail pane, trace pivots, session filter, PartitionKey column — 21 tests.
- Wave 2: `BatchReplayPanel`, `BatchSendPanel`, remap in `AzureServiceBusClient`, wired into `DlqView` and `ServiceBusPage` — 27 tests.

## Remaining

- Wave 3: polish and performance for high-volume queues (session/trace pivot loading, optional bookmarks).

## Blockers

- None.
- Jira is not linked. Informational only.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Wave 1 automated — 17 tests. Manual checks pending.

## Notes

- Trace pivots are explicit identifiers only; reason text is always rendered alongside value.
- Session filter is a pinned overlay filter, not a destructive reload — it works within the current loaded window.
- Batch operations (Wave 2) should remain opt-in and bounded even when the selected message set is large.

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
