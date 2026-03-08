---
title: 'Frontend Plan - Service Bus Enhancements'
owner: ''
status: 'Proposed'
created: '2026-03-08'
updated: '2026-03-08'
---

# Frontend Plan — Service Bus Enhancements

## Quick summary

UI changes to implement quick wins plus interactive flows for edit/resubmit, scheduling, and replay-to-other-namespace.

## Components and UI flow

- `MessageDetailPane.razor`: add "Edit", "Replay", and "Schedule" actions.
- `MessageComposer.razor`: accept an optional `SbMessage` to prefill for edit/resubmit/replay; add target namespace/entity picker and scheduling controls.
- `MessageListView.razor`: add export actions (JSON/CSV/NDJSON) and saved filters menu.
- `ScheduledMessages.razor` (new): list scheduled messages from `ScheduledMessageRepository` with cancel action.
- `KeyboardShortcuts`: extend `wwwroot/js/keyboardShortcuts.js` and wire handlers for quick actions (peek, open detail, edit, resubmit, replay, schedule).

## UX notes

- Auto-detect message body type in `MessageDetailPane` and pretty-render JSON, HTML, text, or show base64 raw with a decode toggle.
- Saved filters per entity: save/load/manage via small dropdown in filter bar; persist to `UiStateRepository`.
- For schedule UI, require user confirmation for delayed sends and show estimated enqueue time.

## Acceptance checks

- Prefill composer from selected message and allow edits for resubmit/replay.
- Export produces correct files for selected messages.
- Scheduled messages appear in `ScheduledMessages` list and can be cancelled.
- Keyboard shortcuts perform their assigned actions in component tests.
