# Archive Summary - service-bus-operator-workbench

---

title: "Archive Summary - service-bus-operator-workbench"
owner: "GitHub Copilot"
jira: "not linked"
completed_date: "2026-04-18"
pr: ""
commit: ""

---

## Goal

Deepen the Service Bus operator experience with richer message triage, safe batch operations, and trace-pivot filtering without adding new top-level routes.

## Delivered

- **Wave 1 — Triage depth:**
  - Enriched `MessageDetailPane` System tab: `PartitionKey`, `ExpiresAt` with TTL-remaining cue, `LockedUntil`.
  - DLQ Info tab: delivery count and expiry in meta block alongside reason and description.
  - Trace Pivot tab: explicit pivots from `CorrelationId`, `MessageId`, `SessionId`, and known app-property keys; per-pivot reason text and Investigate button wired to `IncidentInvestigationLauncher`.
  - Session quick-filter CTA in System tab; `PinnedSessionId` parameter on `MessageListView`.
  - `PartitionKey` column in `MessageListView` column chooser (off by default).
  - 21 tests (18 `MessageDetailPaneTests` + 3 `MessageListViewTests`).

- **Wave 2 — Preview-first batch operations:**
  - `BatchOperationResult`, `BatchOperationItemError`, `BatchSendEntry` models.
  - `ResubmitDeadLetterAsync` updated to accept remap rules (subject, correlationId, property renames/removes via `ApplyRemapRules`).
  - `BatchReplayPanel.razor`: config → confirm (production safety warning) → execute (chunked, progress) → summary flow.
  - `BatchSendPanel.razor`: JSON import → validate → preview → execute (chunked `SendBatchAsync`) → summary.
  - Wired in `DlqView.razor` and `ServiceBusPage.razor`; `AvailableNamespaces` threaded through.
  - 27 tests (15 `BatchReplayPanelTests` + 12 `BatchSendPanelTests`).

- **Wave 3 — Performance and polish:**
  - `TracePivotFilter` parameter on `MessageListView`: applies text filter when pivot value arrives; idempotent.
  - "Filter list" button per trace pivot row in `MessageDetailPane`; conditional on `OnApplyTracePivotFilter.HasDelegate`.
  - Large-window cue: badge + label when loaded message count ≥ 200.
  - Wired in `DlqView` and both `MessageDetailPane` usages in `ServiceBusPage`.
  - 8 tests covering all Wave 3 paths.

- **Total: 56+ tests across `SwebKit.App.Tests`. Build: 0 errors, 0 warnings.**

## Key decisions

- **No new routes** — all new surfaces are panels, tabs, and inline dialogs on the existing Service Bus page. Keeps the operator's mental model stable.
- **Batch operations require a confirm step** — `BatchReplayPanel` shows a count/source/target summary with an explicit production warning before executing. Safety gate is non-negotiable.
- **Trace pivots are explicit identifiers only** — reason text is always rendered alongside the value to prevent operators from misreading a correlation ID as a root cause.
- **`IncidentInvestigationLauncher` requires per-test registration** — added to 3 test host setups to keep component tests isolated.

## Validation performed

- Unit/component tests: 56+ passing in `SwebKit.App.Tests`.
- Build: 0 errors, 0 warnings on net10.0-windows10.0.19041.0.
- Manual: batch replay and send panels validated by user acceptance (2026-04-18).

## Lessons learned

- Multi-line XML doc comments in Razor files cause a compile error — use single-line `///` only.
- When wiring a new EventCallback-based filter (`TracePivotFilter`), idempotency (skip re-apply when value unchanged) must be explicit; without it, a pivot click mid-scroll causes a redundant filter reset.
- Thread `AvailableNamespaces` early through `DlqView` → `BatchReplayPanel`; retrofitting it later touches more files than planning the parameter chain upfront.

## Follow-up

- None. All three waves complete.

## Archive note

> This file is present because the feature had **no Jira ticket** (Path B). Archive location: `docs/features/archive/service-bus-operator-workbench/`.
