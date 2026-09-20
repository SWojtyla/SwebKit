# Service Bus UX Overhaul

## Status

`Review` — see `status.md`.

## Goal

Make the Service Bus workspace pleasant to operate day-to-day: a proper
compose/replay surface instead of the cramped modal, real template management,
and a batch **resend** that clones selected messages with freshly generated
MessageIds instead of reusing the existing GUID.

Driver: user feedback that the composer pop-up is too small and hides fields,
and that resending a message today reuses its MessageId — which Azure Service
Bus duplicate detection silently drops when the entity has dedup enabled.

## Scope

| # | Item | Where |
| - | ---- | ----- |
| 1 | **Batch resend with regenerated MessageId** — new bulk action on the message list in **both Active and DLQ views**. Sends a clone of each selected message with a fresh `messageId`; originals stay in place (copy semantics, not move). DLQ clones strip `DeadLetterReason`/`DeadLetterErrorDescription` application properties. Subscription entities resend to the parent topic. | `web/src/components/service-bus/MessageList.tsx`, new `resendHelpers.ts`, `useServiceBus.ts` |
| 2 | **Composer as resizable side panel** — replace the 600px `MessageComposer` modal with a `SidePanel`/`ResizablePanel` drawer (same pattern as message details). MessageId becomes a visible, editable field with a regenerate button; replay/edit modes default to a **fresh** GUID. Body gets a CodeMirror editor (JSON highlighting via `swebkitHighlighting()`), keeping a hidden mirror textarea for tests. | `MessageComposer.tsx` → panel, `ServiceBusPage.tsx` |
| 3 | **Separate templates manager** — dedicated surface (toolbar entry) with full CRUD: list, search, create blank, edit body/fields/properties, rename, duplicate, delete (confirmed), and "use in composer". Composer keeps a quick-apply entry. Adds "Save as template" to the composer itself (today only reachable from message detail). | new `TemplateManager.tsx`, `TemplatePicker.tsx`, `MessageComposer.tsx` |
| 4 | **Overview & toolbar declutter** — regroup the crowded 6-button toolbar (Compose stays primary; secondary actions into an overflow group), add Templates entry, and give the no-entity-selected pane a useful overview instead of a bare "Select an entity" string (namespace stats, entities with DLQ backlog, jump shortcuts). | `ServiceBusPage.tsx`, new `NamespaceOverview.tsx` |

## Non-goals

- Changes to the legacy MAUI/Blazor app (`src/SwebKit.App`) — reference only.
- New sidecar endpoints: batch resend reuses `POST …/batch-send`; template
  rename/edit/duplicate reuse the existing upsert `POST /api/servicebus/templates`.
- Changing DLQ resubmit semantics — it stays a move operation and already
  regenerates MessageIds server-side.
- Rework of the filter system, saved filters, or column preferences.
- CSV export, message body binary-fidelity improvements (peeked bodies are
  string-mapped; resend inherits that existing limitation).

## Dependencies

- None external. Builds entirely on existing sidecar endpoints and shared UI
  components (`ResizablePanel`, `SearchableSelect`, `ConfirmBar`,
  `NotificationSystem`, `QueryState`).

## Risks

- **CodeMirror theming**: must use `swebkitHighlighting()` — CodeMirror's
  `defaultHighlightStyle` is light-theme only (`docs/pitfalls/react-frontend.md`).
  Playwright cannot read off-screen CodeMirror lines → hidden mirror textarea.
- **Send-target normalization**: send-like actions from a subscription must
  target the parent topic (`isSubscription`/`topicName` on `SbEntityInfo`);
  resending to `topic/subscriptions/name` fails on the broker.
- **Demo mode**: `DemoServiceBusClient.SendBatchAsync` is a no-op — resend in
  demo mode confirms but adds nothing (same as existing send; document it).
- **Duplicate-looking behavior is the feature**: resend deliberately creates
  copies; the confirm dialog must say originals are kept.

## Docs

- `status.md` — checklist and validation
- `technical-plan.md` — component-level implementation plan
- `test-plan.md` — test scenarios
- `decisions.md` — resend semantics, endpoint reuse, panel choices
