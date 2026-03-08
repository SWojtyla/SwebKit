# Technical Plan — Service Bus: UI Polish & UX Improvements

## Status: Done (2026-03-08)

## Overview

This plan addresses two categories of work:
1. **Bug fixes** — broken FluentDataGrid resize handles, ambiguous button labels
2. **UX improvements** — template management, resizable splitter, sortable columns, keyboard nav, copy feedback, empty states

## Bug Fixes

### BUG-1: Table headers clipped + resize handles misaligned

**Root cause:** Conflicting layout strategies — `width: max-content` on `.message-grid` combined with
`table-layout: auto` from `.responsive-message-grid`. FluentDataGrid's resize handles position based
on column widths at render time, but `auto` layout recalculates widths after render, causing the
handles to drift out of alignment.

**Fix:**
- Remove `.message-grid { width: max-content }` and `.responsive-message-grid { table-layout: auto }`
- Switch to `table-layout: fixed` with explicit `Width` on every `<PropertyColumn>` / `<TemplateColumn>`
- Remove per-cell `min-width` / `max-width` classes (`col-message-id`, `col-correlation-id`, etc.)
  since fixed layout + explicit widths handle sizing
- Keep `.cell-truncate` for text overflow on cell content

**Files:**
- `src/SwebKit.App/wwwroot/app.css` — remove/replace grid layout rules
- `src/SwebKit.App/Components/ServiceBus/MessageListView.razor` — add explicit `Width` to all columns

### BUG-2: "Copy JSON" button is ambiguous

**Fix:** Rename to "Copy Full Message" with updated tooltip.

**Files:**
- `src/SwebKit.App/Components/ServiceBus/MessageDetailPane.razor`

## UX Improvements

### UX-1: Save as Template from MessageDetailPane

When viewing a message in the detail pane, add a "Save as Template" button in the header bar.
This creates a template pre-filled with the message's body, content-type, subject, correlation ID,
and application properties.

**Files:**
- `src/SwebKit.App/Components/ServiceBus/MessageDetailPane.razor` — add button + save dialog
- `src/SwebKit.Core/Services/AppStateService.cs` — reuse existing `SaveMessageTemplateAsync`

### UX-2: Enhanced TemplatePicker (rename, edit)

Extend the existing `TemplatePicker.razor` to support:
- **Rename** — inline edit of template name
- **Edit** — open template in a modal with editable body, properties, subject, etc.
- **Duplicate** — clone an existing template with " (copy)" suffix

**Files:**
- `src/SwebKit.App/Components/ServiceBus/TemplatePicker.razor`
- `src/SwebKit.Core/Services/AppStateService.cs` — add `UpdateMessageTemplateAsync`

### UX-3: Resizable splitter between message list and detail pane

Replace the fixed `clamp(280px, 30vw, 400px)` detail pane width with a draggable splitter.
Use a simple `mousedown`/`mousemove`/`mouseup` via JSInterop or a pure-CSS `resize` approach.

**Files:**
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor` — wrap list + detail in splitter layout
- `src/SwebKit.App/Components/ServiceBus/DlqView.razor` — same splitter pattern
- `src/SwebKit.App/wwwroot/app.css` — splitter styles

### UX-4: Sortable columns

Add `Sortable="true"` to relevant columns and provide `SortBy` expressions.

**Files:**
- `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`

### UX-5: Empty state for message list

When `FilteredMessages` is empty and not loading, show a centered illustration/text
instead of an empty grid.

**Files:**
- `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`

### UX-6: Keyboard navigation in message list

- Arrow Up/Down to move selection through visible messages
- Enter to confirm selection (open in detail pane)
- Escape to clear selection

Implemented via `@onkeydown` on the grid container with focus management.

**Files:**
- `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`

### UX-7: Copy feedback toast

After clipboard writes, show a brief "Copied!" indicator near the button.
Use a simple state-driven inline label that auto-clears after 2 seconds via `Task.Delay`.

**Files:**
- `src/SwebKit.App/Components/ServiceBus/MessageDetailPane.razor`

## Implementation Sequence

1. BUG-1: Fix grid layout (table-layout: fixed + explicit widths)
2. BUG-2: Rename Copy JSON button
3. UX-1: Save as Template from detail pane
4. UX-2: Enhanced TemplatePicker
5. UX-3: Resizable splitter
6. UX-4: Sortable columns
7. UX-5: Empty state
8. UX-6: Keyboard navigation
9. UX-7: Copy feedback toast

## Acceptance Checks

- [x] All column headers fully visible at default window size
- [x] Resize handles track column edges accurately after drag
- [x] "Copy Full Message" button label is clear
- [x] Can save a peeked message as template from the detail pane
- [x] Can rename, edit, and duplicate templates in the picker
- [x] Splitter between list and detail pane is draggable
- [x] Columns are sortable by clicking headers
- [x] Empty message list shows a friendly empty state
- [x] Arrow keys navigate the message list
- [x] Copy actions show brief "Copied!" feedback
