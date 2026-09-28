# Service Bus Power Ops — reach-message, DLQ triage, sessions, replay

State: In Progress

## Goal

Four items. Flagship: reach a message deep in a big queue without manually draining
everything ahead of it. Plus DLQ triage, session awareness, and cross-environment replay.

## The honest contract (market this verbatim in the UI)

**Cannot restore**: sequence numbers, queue position, `EnqueuedTime`, `DeliveryCount`,
lock metadata. Every put-back is a new message appended at tail.
**Can restore**: body, subject, correlation/session ids, content type, app properties,
and *relative* order among the restored set.
"Like nothing happened" = payload fidelity + provenance stamp — never positional
fidelity. The consequences preview says this literally.

## Scope

### 1. Reach-message operation (flagship)

Mechanism = **DLQ-park** (beats rotate — 2× broker ops; beats defer — extra
receive-by-seq hop and deferred limbo is invisible in the tool):

```
preview  → validate (RequiresSession→refuse; subscriptions gated to queues v1;
           deferred-target check; prefix estimate)
park     → peek-lock receive batches; seq < target → DeadLetterMessageAsync stamped
           (propertiesToModify: SwebKit.RequeueOp=opId, OriginalSequence,
           OriginalDeliveryCount, OriginalEnqueuedAt — same settlement call, never
           a second step); seq == target → user action (complete/deadletter/resubmit);
           seq > target → abandon, stop. SEQUENCE-BOUND stopping only.
restore  → receive DLQ, filter ApplicationProperties["SwebKit.RequeueOp"]==opId;
           send clone (fresh MessageId, provenance stamp) + complete DLQ copy —
           optionally ambient TransactionScope per message for atomic send+settle.
order    → prefix restored first (original seq order), target's reprocessed copy after
           → tail ≈ original relative order; preview shows resulting order + a
           restoreBeforeTarget choice.
```

- **Endpoints**: `reach-message/preview`, `reach-message/start`, `GET/POST
  .../operations/{id}` (poll, cancel, resume), `GET /operations?entity=` (interrupted).
- **Journal**: `SbOperationJournalRepository` (clone `ScheduledMessageRepository`) —
  accelerator, not source of truth (DLQ stamp scan rediscovers parked set).
- **Safety**: `ReachMessagePanel` wizard = preview→ConfirmBar (`requireTypedName` for
  prefix >100)→progress/cancel. Caps: 1,000 prefix hard cap, opt-in to ~5,000. Cancel
  between batches; in-flight locks expire harmlessly (peek-lock ~5min — UI must say so).
  Crash → "Interrupted operation" banner on entity open (Restore / Leave in DLQ).
- **Failure modes to surface**: competing consumers (undetectable — warn + per-message
  failure tolerance), TTL expiry mid-op (restore skips missing seqs, journal records
  gap), deferred target unreachable by FIFO (detect + offer `ReceiveDeferred` path).

### 2. DLQ triage workflow

- `DlqTriagePanel`: client-side group-by `(deadLetterReason, description)` over the
  peeked window → group chips → select-all-in-group → existing chunked bulk bar.
- Beyond the 250-peek window: `POST .../dlq/requeue-by-filter {reason, description,
  limit}` — server-side receive→match→resend→complete (sibling of
  `MessageSequenceProcessor` with a predicate).
- **Resend-with-edit fix**: `POST .../dlq/resubmit-edited {seq, message, targetPath?}` —
  receive→send edited clone→complete original. Wires `MessageDetail`'s "Edit & Resubmit"
  to actually settle the original — fixes today's copy-only duplicate trap.

### 3. Session awareness

- `SbEntityInfo.RequiresSession` from `QueueProperties`/`SubscriptionProperties`
  (zero extra calls — already in the pageable payloads).
- `PeekSessionsAsync` — peek window grouped by `SessionId` (no session locks; SDK has
  no session enumeration).
- UI: session badge + gate mutating buttons with explanation (today: opaque 502).
  Hard prerequisite for the flagship op.

### 4. Replay to another environment

- `POST .../replay-to {seqs, deadLetter, targetNsId, targetEntityPath, scrubProperties,
  removeSource}` — pooled client per nsId, send to target, optional source complete.
- `scrubProperties`: drop all `ApplicationProperties` + DLQ metadata (kills
  NServiceBus.*/routing/stamps); keep body/contentType/subject/correlationId.
- UI reuses composer's `targetNsId` + `EntityPathInput` (precedent exists).
- Documented loss: To/ReplyTo/TTL/PartitionKey don't survive `SbMessage` mapping.

## Non-goals

- Session-entity mutation support (per-session receivers); positional restore (impossible
  on the broker); agent `propose_requeue` tool (natural later add via `AgentActionType`).

## Implementation tasks

- [x] Models: `RequiresSession`, `SbSessionSummary`, resubmit-edited request
      (commit d6b51aae); preview/op/journal records, replay request still open
- [x] `IServiceBusClient`: PeekSessions, ResubmitEdited (throwing defaults);
      park/restore primitives via `ReachParkProcessor` (commit 139fa291);
      entity props + cross-env Replay still open
- [x] `SbOperationService` (journaled park→act→restore, stamp-scan crash
      recovery, cancel/resume/dismiss endpoints) (commit 139fa291)
- [x] `ReachMessagePanel` wizard + progress polling; `DlqTriagePanel`
      (commit 139fa291); `ReplayToPanel` (cross-env requeue) still open
- [x] composer `editResubmit` mode + session badges/gating (commit d6b51aae)
- [x] Demo client: honest resubmit/complete + `order-sessions` session-flagged
      demo entity (commit d6b51aae)

## Test plan

- Azure tests (fake-receive harness exists): seq-bound stop, overshoot abandoned,
  missing target aborts, restore stamps+orders, session refusal, edited-body resubmit.
- Sidecar tests: op state machine, cancel mid-park, resume-after-crash via journal.
- vitest: DLQ grouping, reach-message consequence/cap helpers.
- e2e (demo): reach-message end-to-end, triage group→bulk resubmit, session gating,
  interrupted-op banner. ⚠ demo re-seeds on toggle.

## Risks (blunt)

1. Races live consumers — undetectable; warn + per-message tolerance.
2. Restore never positional — support burden if the UI oversells "clean".
3. DLQ pollution if stamping skipped — stamp via `propertiesToModify` in the same
   settlement call, never separately.
4. Session entities break every settle path — `RequiresSession` gating ships first.
5. Demo no-ops make e2e lie — implement them honestly or gate.
6. Peek-count clamp (250) caps triage window — beyond it needs the filter endpoint.
7. `TransactionScope` atomicity: verify package 7.20.2 behavior; never across
   namespaces (replay stays two-hop).
