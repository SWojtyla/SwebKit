# Decisions — Service Bus UX Overhaul

## D1 — Batch resend is copy semantics, implemented client-side via `/batch-send`

**Decision:** resend clones the already-peeked `SbMessage` objects, assigns a
fresh `crypto.randomUUID()` per clone, strips DLQ app-prop keys, and posts them
through the existing `POST …/batch-send` endpoint. Originals are never touched.

**Considered:** a dedicated `POST …/resend` endpoint taking sequence numbers
that re-peeks and clones server-side (mirroring `ResubmitDeadLetterAsync`).
Rejected: peeked messages already carry full bodies + properties, so a new
endpoint adds surface and tests for zero behavioral gain. Copy semantics also
can't reuse the resubmit path anyway — that one completes (removes) the source.

**Note:** client-side resend can only resend messages already loaded in the
peek window — which is exactly the set the checkboxes can select, so no
practical limitation.

## D2 — Replay/edit defaults to a fresh MessageId, shown and editable

Today `MessageComposer` initializes `messageId` from `sourceMessage` in
replay/edit modes — resends with an unchanged ID get silently dropped by
broker duplicate detection. The field is now visible, defaults to a fresh
GUID in every mode, and has a regenerate button. A user who genuinely wants
the original ID can paste it — the field is editable, not hidden.

## D3 — Composer becomes a `SidePanel`/`ResizablePanel` drawer

Same surface pattern as message details (push on wide screens, overlay on
narrow), resizable with a persisted `storageKey`. Rejected alternatives: a
larger centered modal (still modal, still fixed) and a dedicated route (loses
the list context that replay/edit workflows need).

## D4 — Template CRUD reuses the existing upsert endpoint; no schema change

`POST /api/servicebus/templates` already upserts by `Id`
(`ProfileRepository.SaveMessageTemplate`), so rename/edit/duplicate are pure
frontend compositions of it. No `updatedAt` field added — keeps
`ProfileData`/`SchemaVersion` untouched.

## D5 — Subscription entities resend/send to the parent topic

`SbEntityInfo.isSubscription` + `topicName` drive `sendableEntityPath()` —
a subscription path is receive-only; this matches the existing rule that
send-like actions from a subscription workspace normalize to the parent topic.

## D6 — Templates live in a separate manager surface

User-chosen: a dedicated manager (toolbar entry) with full CRUD rather than
only an in-composer strip. The composer keeps a quick-apply entry point that
opens the manager in pick mode so there is one list implementation.

## D7 — DLQ clones strip `DeadLetterReason`/`DeadLetterErrorDescription`

The broker writes these into `ApplicationProperties` on dead-lettered
messages; `ResubmitDeadLetterAsync` removes them before forwarding, and the
client-side clone does the same so a resend doesn't carry stale DLQ metadata.
