# Frontend Implementation — Service Bus UX Overhaul

All work is in `web/src/components/service-bus/` plus `web/src/lib/hooks/useServiceBus.ts`.
No sidecar endpoint changes (see `decisions.md` D1/D4).

## A — Batch resend (`resendHelpers.ts`, `MessageList.tsx`, `useServiceBus.ts`)

### `resendHelpers.ts` (new, next to `exportHelpers.ts`)

```ts
const DLQ_PROP_KEYS = ["DeadLetterReason", "DeadLetterErrorDescription"];

export function cloneForResend(source: SbMessage): SbMessage {
  const appProps = { ...source.applicationProperties };
  for (const k of DLQ_PROP_KEYS) delete appProps[k];
  return {
    ...source,
    messageId: crypto.randomUUID(),
    applicationProperties: appProps,
    sequenceNumber: null,
    lockToken: null,
    deadLetterReason: null,
    deadLetterErrorDescription: null,
    deliveryCount: 0,
    enqueuedAt: new Date().toISOString(),
  };
}

/** Send target for an entity: subscriptions are receive-only, so send to the parent topic. */
export function sendableEntityPath(entity: SbEntityInfo): string {
  return entity.isSubscription && entity.topicName ? entity.topicName : entity.entityPath;
}
```

Mirrors the server-side clone in `AzureServiceBusClient.ResubmitDeadLetterAsync`
(`new ServiceBusMessage(message) { MessageId = Guid.NewGuid() }` + DLQ-prop strip)
— but without completing the originals.

### `useServiceBus.ts`

Reuse `useSbBatchSend`. If a resend-specific mutation is wanted for naming,
`useSbResendMessages` wraps it: clones in `mutationFn`, posts to
`/batch-send` against `sendableEntityPath(entity)`, and on success calls
`invalidateServiceBusQueries(qc, nsId, entity.entityPath)` (peek + dlq + stats;
**not** `includeTopology`).

### `MessageList.tsx`

- Extend `pendingBulkConfirm` kind union with `"resend"`.
- Bulk bar gains a `Resend` button (icon `Send`/`CopyPlus`) shown in **both**
  view modes — unlike `Resubmit` which is DLQ-only.
- Confirm message: `Send a copy of N message(s) to <path>? Originals are kept.
  Each copy gets a new Message ID.`
- On success: `notify("success", "Resent N message(s) as new copies")`, clear
  `selectedMsgs`, invalidate as above.

## B — Composer → resizable side panel (`MessageComposer.tsx`, `ServiceBusPage.tsx`)

- Mount via `SidePanel` (wraps `ResizablePanel`) instead of the fixed
  `composer-overlay` div: `defaultWidth≈640`, `minWidth` that keeps all controls
  usable, `maxWidth≈1400`, `storageKey="service-bus-composer"`, `title` per mode.
  Same push/overlay behavior as the message-detail panel.
- Keep all existing `data-testid`s (`composer-*`) so e2e specs keep working.
- **MessageId field** (new row, likely read-mostly input + regenerate icon):
  - `compose`/`schedule`: `crypto.randomUUID()` (unchanged).
  - `replay`/`edit`: default `crypto.randomUUID()` — the fix for dedup-silent
    drops. Editable so the original can be pasted back deliberately.
- **Body editor**: extract a small `MessageBodyEditor` modeled on
  `components/api-client/request-editor/BodyCodeEditor.tsx`:
  - `@codemirror/lang-json` (switch on `contentType`/`detectFormat`), mount-once
    `EditorView`, `Compartment` for language reconfigure.
  - `swebkitHighlighting()` — never `defaultHighlightStyle` (light-only pitfall).
  - Hidden `aria-hidden` mirror `<textarea data-testid="composer-body">` so
    Playwright/AT can read full text (CodeMirror only renders the viewport).
  - Keep `composer-format-json`.
- Template quick-apply stays: `composer-load-template` opens the picker (now the
  manager's pick mode) without closing the composer.
- Add `composer-save-template`: name prompt → `useSbSaveTemplate` upsert.
- Layout: keep the 2-col field grid but ensure no clipped controls at min panel
  width; properties editor unchanged in structure.

`ServiceBusPage.tsx`: render `{composerMode && <SidePanel …><MessageComposer …/></SidePanel>}`
alongside the detail `SidePanel` usage; keep `composerMode` state shape.

## C — Templates manager (`TemplateManager.tsx`, `TemplatePicker.tsx`)

New `TemplateManager` component — a large dialog or `SidePanel` opened from a
new toolbar `Templates` button:

- Left: searchable template list (reuse `TemplatePicker` row rendering).
- Right: editor form — name, subject, correlationId, contentType, CodeMirror
  body, properties key/value rows.
- Actions: New, Save (upsert same id), Rename (inline or via edit form),
  Duplicate (`{...t, id: crypto.randomUUID(), name: t.name + " (copy)"}`),
  Delete (existing `ConfirmBar` pattern), "Use in composer" →
  `onSelect(template)` into the composer panel.
- `TemplatePicker` can either embed the manager in pick mode or keep as-is for
  the composer quick-apply — prefer reusing the manager with an `onPick` prop
  to avoid two list implementations.
- All writes through `useSbSaveTemplate` (upsert) / `useSbDeleteTemplate`;
  `sb-templates` query key invalidated by the existing hooks.

## D — Overview & toolbar (`ServiceBusPage.tsx`, `NamespaceOverview.tsx`)

- Toolbar: keep `Namespace` selector + `Compose` (primary) + `Search Entities`
  + `Templates`. Group `Batch Send`, `Scheduled`, `Batch Replay` into an
  `Actions` dropdown (or icon-button group) — all currently peer buttons at
  ~6 items on one row. Every control keeps its `data-testid`; add
  `sb-templates-button`, `sb-actions-menu`.
- `NamespaceOverview` (new): rendered in the middle pane when
  `selectedEntity === null` — namespace info (`useSbNamespaceInfo`), totals
  (entities, active, DLQ, scheduled aggregated from loaded queues/topics),
  and a "needs attention" list of entities with `deadLetterMessageCount > 0`
  that select the entity in DLQ view on click. Replaces the bare
  "Select an entity" empty string in `MessageList` — keep that fallback for
  safety but render the overview at page level.

## Pitfall checklist (from docs/pitfalls)

- `swebkitHighlighting()` for every CodeMirror instance; theme via CSS vars.
- Hidden mirror textarea for CodeMirror testability.
- `invalidateServiceBusQueries` — never `queryKey: ["sb-"]` prefix matching.
- `useNotification` on every mutation success/error.
- `data-testid` on every state-changing control, not only containers.
- Panel `minWidth` arithmetic must fit a 1280px window.
- Disabled buttons carry a `title` reason; loading states visible.
- SSE/EventSource not involved; no new Tauri plugins — no capability changes.
- URL search params: batch `updateParams` calls; don't snapshot-clobber
  (`window.location.search` caveat in `react-frontend.md`).
