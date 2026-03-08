# Technical Plan — Service Bus: UI

## Status

- Namespace panel (add / expand / remove): **Done**
- EntityTree (queues / topics / subscriptions): **Done**
- Tab system (open / close / DLQ): **Done**
- Pin / unpin entity (📍 / 📌): **Done**
- Demo namespace (FakeServiceBusClient): **Done**
- Focused UI bug fix pack (4 user-reported defects): **Done** (2026-03-08)
- DLQ multi-select and batch action bar: **Done** (2026-03-08)
- Message composer (`MessageComposer.razor`): **Done** (2026-03-08)
- Template picker (`TemplatePicker.razor`): **Done** (2026-03-08)
- Auto-refresh interval selector: **Done** (2026-03-08)
- Export / Copy JSON from detail pane: **Done** (2026-03-08)
- Scenario editor: Pending
- Filter-state persistence by entity path: Pending

## Component Hierarchy

```
ServiceBusPage (Pages/)
├── namespace panel
│   └── EntityTree (ServiceBus/)
│       ├── LoadingSpinner (Shared/)
│       └── ErrorCallout (Shared/)
└── tab area
    ├── DlqView (ServiceBus/)
    ├── MessageListView (ServiceBus/)
    │   └── PropRow (ServiceBus/)
    └── MessageDetailPane (ServiceBus/)
        └── PropRow (ServiceBus/)
```

## Blazor Patterns & Pitfalls

See [`docs/pitfalls/blazor-maui.md`](../../pitfalls/blazor-maui.md) for the full reference. Entries most relevant here: **BL-1** (`_Imports.razor`), **BL-2** (`InvokeAsync`), **BL-3** (guard before `await`), **BL-4** (`@if` destroy/recreate), **BL-5** (`OnParametersSetAsync` frequency).

## Focused Bug Plan

For the user-reported Service Bus UI defects (DLQ count/render mismatch, table truncation/scroll, left-panel scroll interference, and encoded topic labels), use:

- `docs/features/service-bus/technical-plan-ui-bugfixes.md`

## Implementation Sequence

1. ~~Build namespace panel with expand/collapse and add form.~~ **Done**
2. ~~Build `EntityTree` with queues / topics / subscriptions.~~ **Done**
3. ~~Build tab system and message inspector panes.~~ **Done**
4. ~~Add pin/unpin controls to entity rows.~~ **Done**
5. ~~Add demo namespace with `FakeServiceBusClient`.~~ **Done**
6. ~~Add DLQ multi-select with batch action bar.~~ **Done**
7. ~~Build message composer with property and body editors.~~ **Done**
8. ~~Build template picker and management sheet.~~ **Done**
9. Build scenario editor with step list.
10. ~~Add auto-refresh interval selector.~~ **Done**
11. Add filter-state persistence by entity path.
12. ~~Add export and clipboard actions.~~ **Done**

## Detailed Tasks

- [x] Add multi-select checkboxes and sticky action bar to `DlqView`.
  - Files: `src/SwebKit.App/Components/ServiceBus/DlqView.razor`
  - Done: per-row checkboxes in `MessageListView` (MultiSelect=true) + batch resubmit/delete bar in DlqView.
- [x] Build `MessageComposer.razor` with body editor and property key/value table.
  - Files: `src/SwebKit.App/Components/ServiceBus/MessageComposer.razor` (new)
  - Done: inline panel in MessageListView filter bar (✉ Send toggle).
- [x] Build `TemplatePicker.razor` with save / load / delete.
  - Files: `src/SwebKit.App/Components/ServiceBus/TemplatePicker.razor` (new)
  - Done: modal overlay listing templates with Apply / Delete; wired into MessageComposer.
- [ ] Build `ScenarioEditor.razor` with ordered step list and run/cancel controls.
  - Files: `src/SwebKit.App/Components/ServiceBus/ScenarioEditor.razor`
- [x] Add auto-refresh interval selector to `MessageListView`.
  - Files: `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`
  - Done: dropdown (Off / 10s / 30s / 60s) using `System.Timers.Timer`; component implements `IDisposable`.
- [ ] Persist active filter state (search text, mode) keyed by entity path in `UiStateRepository`.
  - Files: `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- [x] Add copy-to-clipboard actions to `MessageDetailPane`.
  - Files: `src/SwebKit.App/Components/ServiceBus/MessageDetailPane.razor`
  - Done: Copy Body (existing) + Copy JSON (full message envelope as indented JSON).
- [x] Enforce `ConfirmDialog` for all mutative actions when `IsProduction` is true.
  - Files: `src/SwebKit.App/Components/ServiceBus/DlqView.razor`, `MessageComposer.razor`
  - Done: ConfirmDialog used in DlqView single + batch ops, and in MessageComposer send flow.

## Acceptance Checks

- [x] Namespace panel shows green dot on successful connection.
- [x] Entity tree renders queues, topics, and subscriptions correctly.
- [x] Tabs open and close; DLQ tab is distinct from peek tab.
- [x] Pin/unpin icons update immediately and persist.
- [x] Demo namespace shows fake data end-to-end.
- [x] DLQ multi-select and batch action bar work correctly.
- [x] Composer sends messages with custom body and properties.
- [x] Templates save, load, and pre-fill the composer.
- [ ] Scenario steps execute and show progress.
- [ ] Auto-refresh pauses when the tab is hidden (currently timer-based; visibility-pause not yet implemented).
- [x] Production confirm dialog blocks accidental mutations.

## UI Polish & UX Improvements

See [`technical-plan-ui-polish.md`](technical-plan-ui-polish.md) for the full plan covering:
- Grid layout fix (headers clipped, resize handles misaligned)
- Button label clarity ("Copy JSON" → "Copy Full Message")
- Save message as template from detail pane
- Enhanced template management (rename, edit, duplicate)
- Resizable splitter between message list and detail pane
- Sortable columns, empty states, keyboard navigation, copy feedback

## Traceability Backlinks

- `docs/features/service-bus/index.md`
- `docs/features/service-bus/technical-plan-ui-bugfixes.md`
- `docs/features/service-bus/technical-plan-ui-polish.md`
- `docs/features/service-bus/technical-plan-backend.md`
- `docs/features/service-bus/test-plan.md`
