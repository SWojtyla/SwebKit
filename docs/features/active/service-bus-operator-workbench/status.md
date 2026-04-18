# Status - service-bus-operator-workbench

---

title: "Status - service-bus-operator-workbench"
owner: "GitHub Copilot"
state: "Done"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-17"

---

## Quick summary

Wave 1 (triage depth) is complete. Wave 2 (preview-first batch operations) is complete. Wave 3 (bounded large-volume loading cues and trace pivot filter) is complete. All three waves are fully implemented, tested, and the build is clean (0 warnings, 0 errors). 118 automated tests passing across all Service Bus and related components.

Jira: not linked

Current focus: Done — feature complete, ready for archive or pre-ship review.

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

### Wave 3 - performance and polish ✅

- [x] `TracePivotFilter` parameter on `MessageListView`: applies text filter when pivot value arrives from detail pane; idempotent (same value does not re-apply)
- [x] "Filter list" button on each trace pivot row in `MessageDetailPane`; conditional on `OnApplyTracePivotFilter.HasDelegate`
- [x] `OnApplyTracePivotFilter` wired in `DlqView` and both `MessageDetailPane` usages in `ServiceBusPage`
- [x] Large-window cue: badge + "Large window" label appears when loaded message count ≥ 200 (`LargeWindowThreshold`)
- [x] 8 new tests in `MessageDetailPaneTests` + `MessageListViewTests` covering all Wave 3 paths

## Completed

- Wave 1: enriched detail pane, trace pivots, session filter, PartitionKey column — 21 tests.
- Wave 2: `BatchReplayPanel`, `BatchSendPanel`, remap in `AzureServiceBusClient`, wired into `DlqView` and `ServiceBusPage` — 27 tests.
- Wave 3: `TracePivotFilter` parameter + "Filter list" button on trace pivot rows, large-window cue, full wiring in `DlqView` and `ServiceBusPage` — 8 tests.
- Infrastructure: fixed compile error (multi-line XML doc comment in Razor), suppressed pre-existing deprecated API warning (BlazorMonaco `AddAction`), added `SkeletonRows` to test project, added `IncidentInvestigationLauncher` registration to 3 test classes, fixed test `_Imports.razor` and csproj gaps. Build: 0 errors, 0 warnings.

## Remaining

- None. Feature is complete.

## Blockers

- None.
- Jira is not linked. Informational only.

## Validation

- Test Plan: `test-plan.md`
- Validation status: All waves automated — 56+ tests in `SwebKit.App.Tests`. Build clean.

## Notes

- Trace pivots are explicit identifiers only; reason text is always rendered alongside value.
- Session filter is a pinned overlay filter, not a destructive reload — it works within the current loaded window.
- Batch operations (Wave 2) should remain opt-in and bounded even when the selected message set is large.
- Large-window threshold is 200 (matches max single-peek window). Cue is informational, not blocking.

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
