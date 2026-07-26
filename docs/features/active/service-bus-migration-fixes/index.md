# Service Bus Feature — Migration Bug Fixes

## Summary

The Service Bus feature was ported from MAUI Blazor
(`src/SwebKit.App/Components/Pages/ServiceBusPage.razor`,
`src/SwebKit.App/Components/ServiceBus/ServiceBusGrid.razor`) to React
(`web/src/components/service-bus/*`). Code review found the core message plumbing
(peek/complete/resubmit/schedule/templates) is faithfully and correctly migrated — but the entity
tree lost a MAUI drill-down affordance, and a brand-new "Batch Replay" feature was added that isn't
actually a port of anything from MAUI and doesn't work end-to-end against the current sidecar
contract. This — not the core message plumbing — is why Service Bus "no longer resembles the MAUI
version" per the user's report.

**Jira:** not linked

**Depends on:** [tauri-security-hardening](../tauri-security-hardening/index.md) landing first.

## Scope

Fix the Batch Replay feature (or scope it down to what the sidecar can actually support) and
restore the entity tree's lost drill-down/data-display behavior.

## Non-Goals

- Not touching the message list, message detail, composer, templates, or scheduled messages —
  review found these correctly migrated with no changes needed.

## Tasks, in priority order

### 1. Fix Batch Replay's broken "Active" source (Critical)

**File:** `web/src/components/service-bus/BatchReplayPanel.tsx:52-66`.

`handleReplay` always calls `useSbResubmitDlq`, which hits
`POST /api/servicebus/{ns}/entities/{path}/resubmit` → `ResubmitDeadLetterAsync`, whose receiver is
hardcoded to `{entityPath}/$DeadLetterQueue` (`AzureServiceBusClient.cs:379-395`). When the user
picks `sourceView: "active"` (line 16-29) and selects active-queue messages, those sequence numbers
don't exist in the DLQ receiver — the backend throws, and because the `try { } finally { ... }` at
line 52-66 has **no `catch`**, the rejection is unhandled and the UI silently sits there with
`replaying` never clearing.

**Fix, pick one:**
- (a) If "replay from Active" is meant to mean "resend a copy of these active messages," wire it to
  the existing `send` endpoint instead of `resubmit` (send a new message cloned from the peeked
  active message's body/properties), not the DLQ-only resubmit path.
- (b) If it's out of scope for now, remove the "Active" option from `sourceView` entirely rather
  than presenting a control that always fails.

Either way, add a `catch` that surfaces a real error to the user instead of failing silently.

### 2. Fix or remove the target namespace/entity pickers (Critical)

**File:** `BatchReplayPanel.tsx:14-15` (state), `57-61` (used in the mutation call — but it's not:
it uses the *original* closure `nsId`/`entity.entityPath`, never the selected target), `108-132`
(the `<select>` UI), `33-35` (`targetEntities` hardcoded to `[entity.entityPath]`, so the entity
dropdown can never actually offer more than one option).

This presents cross-namespace/cross-entity replay as a real feature when it cannot work even in
principle with the current backend contract — the `/resubmit` route is namespace/entity-scoped in
the URL. **Fix, pick one:**
- (a) If cross-namespace/entity replay is wanted, it needs a new sidecar endpoint that accepts an
  explicit target namespace connection + entity path and does source-peek → target-send, decoupled
  from the current same-entity `/resubmit` route. This is real backend work, not a quick fix.
- (b) If it's out of scope for now (recommended given this is net-new, not a MAUI port), remove the
  target namespace/entity pickers entirely and make Batch Replay operate within the same
  entity/DLQ it's opened from — matching what the backend can actually do today.

**Test (whichever option):** run a batch replay in demo mode, confirm messages actually move/appear
where the UI claims they will, and confirm a deliberately-broken input (e.g. an empty sequence
number list) shows a clear error rather than a silent hang.

### 3. Restore entity tree drill-down and scheduled-count display (Major)

**File:** `web/src/components/service-bus/EntityTree.tsx`.

- **Dead "Sch" column** (lines 12, 24-41, 125-127): sortable by `scheduledMessageCount`, but
  `EntityBadges` (lines 79-95) never renders it — only active/DLQ counts. Compare to MAUI's
  `ServiceBusGrid.razor:106-109,122`, which both sorts *and* displays it. **Fix:** render the
  scheduled count badge alongside active/DLQ.
- **Lost clickable badges** (lines 82-93 are plain `<span>`s): MAUI's `ServiceBusGrid.razor:459-490`
  made the Active/DLQ count badges clickable buttons that jumped straight into that view
  (`RenderCount(..., onclick => OpenEntity(ns, entity, isDlq))`). Now every row always opens
  whichever `viewMode` tab is currently active on the page regardless of which badge was clicked.
  **Fix:** make the badges clickable again, opening the entity directly into the Active or DLQ view
  matching the badge clicked.

**Test:** in the entity tree, click a queue's DLQ badge and confirm it opens directly into the DLQ
view for that queue (not whatever tab was last active); confirm the scheduled count is visible and
matches what `MessageList.tsx`'s scheduled view shows for the same entity.

### 4. Minor cleanup

- `EntityTree.tsx:59`: `if (queuesLoading && topicsLoading)` should likely be `||` — as written, if
  one query resolves before the other, the tree can render treating the still-loading side as
  empty, flashing "No entities found" or an incomplete list. Pre-existing, but the new
  filter/sort/badge work makes the flash more visible — worth fixing in the same pass.
- Re-indent the Topics block in `EntityTree.tsx` (~lines 130-197) to match the Queues block's
  nesting — no functional bug, just diff/readability noise from the recent edit.
- `web/src/lib/stores/sb-preferences.ts` has no cleanup for orphaned `localStorage` keys when a
  namespace is removed/re-added with a new GUID. Low priority — add a TODO or a light cleanup pass
  (e.g. prune keys for namespace IDs not in the current profile on load) if convenient, not worth
  its own task otherwise.

## Dependencies

- [tauri-security-hardening](../tauri-security-hardening/index.md) (sidecar call auth changes).
- `src-sidecar/Endpoints/ServiceBusEndpoints.cs` / `AzureServiceBusClient.cs` — Task 1 option (a)
  and Task 2 option (a) both need backend changes here if chosen.

## Risks

| Risk | Mitigation |
|---|---|
| Batch Replay's "real" cross-entity design (Tasks 1a/2a) is a bigger backend change than expected | Default to the scoped-down options (1b/2b) first to unblock the release; file real cross-entity replay as its own future feature if wanted |
| Badge click-to-drill-down (Task 3) conflicts with the existing tab-based `viewMode` navigation | Match MAUI's exact behavior (badge click sets both the open entity AND the view mode) rather than inventing new navigation semantics |
