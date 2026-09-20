# Test Plan — Service Bus UX Overhaul

Scope: `web/` React UI + reuse of existing sidecar endpoints. Demo mode is the
primary e2e data source; enable it via the header toggle before SB specs.

## Batch resend

| # | Scenario | Expected |
| - | -------- | -------- |
| R1 | Select 3 active messages → Resend → confirm | 3 clones sent to same entity; each gets a distinct new `messageId` ≠ original; originals still listed |
| R2 | Same in DLQ view | Clones land on the entity's active side; `DeadLetterReason`/`DeadLetterErrorDescription` app props stripped; DLQ originals still present |
| R3 | Resend from a subscription entity | Sends to the parent topic, not the subscription path |
| R4 | Resend with 0 selected | Action disabled / not reachable |
| R5 | Confirm dialog wording | States copies are sent and originals are kept; cancel aborts cleanly |
| R6 | Failure mid-resend | Error toast via `useNotification`; list state consistent after refresh |
| R7 | Dedup-enabled entity | Resent messages actually arrive (fresh GUIDs bypass dedup) — manual/integration check |

Unit: `cloneForResend` — new UUID per call, DLQ keys removed, `sessionId`/
`subject`/`correlationId`/`contentType`/`applicationProperties` preserved,
broker-owned fields nulled.

## Composer panel

| # | Scenario | Expected |
| - | -------- | -------- |
| C1 | Compose from toolbar | Opens resizable side panel, not a centered modal; message list stays visible |
| C2 | Replay a message | MessageId field shows a **fresh** GUID, not the source message's |
| C3 | Edit MessageId / click regenerate | Value editable; regenerate mints a new GUID; explicit paste of old ID works |
| C4 | Body editing | JSON syntax highlighting in dark theme; Format JSON works; full text assertable via hidden mirror element |
| C5 | Panel resize | Draggable; remembers width (`storageKey`); controls usable at min width and at 1280px window |
| C6 | Save as template | From composer: names + persists template; appears in manager |
| C7 | Apply template | Fills body/subject/correlationId/contentType/properties without closing composer |
| C8 | Schedule mode | Datetime field + send disabled until valid; same surface |

## Templates manager

| # | Scenario | Expected |
| - | -------- | -------- |
| T1 | Open from toolbar | Manager lists templates with search |
| T2 | Create blank | New template persisted via upsert; appears in composer quick-apply |
| T3 | Edit + rename | Updates persist across panel reopen (profile round-trip) |
| T4 | Duplicate | New template with new id, "copy" name, identical content |
| T5 | Delete | ConfirmBar first; removed from list and from composer apply |
| T6 | Empty state | Clear guidance + create action; loading and error states distinct |

## Overview / toolbar

| # | Scenario | Expected |
| - | -------- | -------- |
| O1 | Toolbar at 1280px | All controls reachable; secondary actions grouped; nothing clipped |
| O2 | No entity selected | Overview pane shows namespace summary + DLQ-backlog entities as jump targets instead of bare "Select an entity" |
| O3 | Deep links / back-forward | `?ns/?entity/?view/?msg/?seq` URL state unchanged |
| O4 | Entity switch | Resend selection cleared; composer state doesn't leak across entities |

## Regression

- Existing `web/e2e/service-bus.spec.ts` and `service-bus-url-state.spec.ts`
  updated for the panel-based composer (testids preserved where possible).
- Purge / bulk complete / bulk resubmit / export / filters unchanged.
- `data-testid` present on every new interactive control.
