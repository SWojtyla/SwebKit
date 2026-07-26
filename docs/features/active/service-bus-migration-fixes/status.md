# Status — Service Bus Feature Migration Bug Fixes

## Current State

`Done` (pending user commit)

## Quick Summary

The core Service Bus message plumbing (peek/complete/resubmit/schedule/templates) is correctly
migrated and needs no changes. Fixes here target the new Batch Replay panel (broken/half-wired,
not a MAUI port) and the entity tree's lost drill-down + scheduled-count display (a real
regression vs. MAUI).

**Jira:** not linked

## Progress Checklist

- [x] Batch Replay scoped down to DLQ-only (source toggle removed) — resubmit is the only operation
      the sidecar contract actually supports; a real "replay from Active" would need a new
      send-based endpoint, out of scope for this pass
- [x] Batch Replay error handling: `catch` added with a visible error banner, failures no longer
      fail silently
- [x] Batch Replay target namespace/entity pickers removed (were non-functional — selections were
      silently discarded, and the entity dropdown could never offer more than one option anyway)
- [x] EntityTree: scheduled-count badge rendered (not just sortable-but-invisible)
- [x] EntityTree: Active/DLQ/Scheduled badges clickable, opening directly into that view (same fix
      applied to topic subscriptions for consistency)
- [x] EntityTree: `queuesLoading && topicsLoading` → `||`
- [x] EntityTree: Topics block indentation cleaned up
- [x] Message list rewritten as a real multi-column data table (Enqueued/Seq/Message ID/Correlation
      ID/Subject/Delivery/Content Type/Session/Partition Key/DLQ Reason columns), matching the
      MAUI grid's dense spreadsheet layout instead of a stacked card per message — column
      visibility toggle, custom property columns, and row density all preserved
- [x] Automated smoke test in demo mode: full Playwright suite (`service-bus.spec.ts`) — 20/20
      passing, covering entity tree drill-down, full message lifecycle (peek → edit/replay/schedule
      → complete), batch send, templates, scheduled messages, entity command palette

## Validation

Not started.

## Blockers

Waiting on [tauri-security-hardening](../tauri-security-hardening/status.md) to land first.

## Notes

- Found during code review on 2026-07-26 of uncommitted changes on `feat/tauri-react-rewrite`.
- MAUI reference: `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`,
  `src/SwebKit.App/Components/ServiceBus/ServiceBusGrid.razor`.
