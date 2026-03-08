# Technical Plan — Service Bus: UI

## Status

- Namespace panel (add / expand / remove): **Done**
- EntityTree (queues / topics / subscriptions): **Done**
- Tab system (open / close / DLQ): **Done**
- Pin / unpin entity (📍 / 📌): **Done**
- Demo namespace (FakeServiceBusClient): **Done**
- Focused UI bug fix pack (4 user-reported defects): **Planned**
- DLQ multi-select and action bar: Pending
- Message composer: Pending
- Template picker: Pending
- Scenario editor: Pending
- Auto-refresh and filter persistence: Pending

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
6. Add DLQ multi-select with batch action bar.
7. Build message composer with property and body editors.
8. Build template picker and management sheet.
9. Build scenario editor with step list.
10. Add auto-refresh toggle and visibility-aware pause.
11. Add filter-state persistence by entity path.
12. Add export and clipboard actions.

## Detailed Tasks

- [ ] Add multi-select checkboxes and sticky action bar to `DlqView`.
  - Files: `src/SwebKit.App/Components/ServiceBus/DlqView.razor`
- [ ] Build `MessageComposer.razor` with body editor (Monaco) and property key/value table.
  - Files: `src/SwebKit.App/Components/ServiceBus/MessageComposer.razor`
- [ ] Build `TemplatePicker.razor` with save / load / delete.
  - Files: `src/SwebKit.App/Components/ServiceBus/TemplatePicker.razor`
- [ ] Build `ScenarioEditor.razor` with ordered step list and run/cancel controls.
  - Files: `src/SwebKit.App/Components/ServiceBus/ScenarioEditor.razor`
- [ ] Add auto-refresh interval selector and `IsBrowserVisible` pause to `MessageListView`.
  - Files: `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`
- [ ] Persist active filter state (search text, mode) keyed by entity path in `UiStateRepository`.
  - Files: `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- [ ] Add export (JSON / CSV) and copy-to-clipboard actions to `MessageDetailPane` and `DlqView`.
  - Files: `src/SwebKit.App/Components/ServiceBus/MessageDetailPane.razor`, `DlqView.razor`
- [ ] Enforce `ConfirmDialog` for all mutative actions when `IsProduction` is true.
  - Files: `src/SwebKit.App/Components/ServiceBus/DlqView.razor`, `MessageComposer.razor`

## Acceptance Checks

- [x] Namespace panel shows green dot on successful connection.
- [x] Entity tree renders queues, topics, and subscriptions correctly.
- [x] Tabs open and close; DLQ tab is distinct from peek tab.
- [x] Pin/unpin icons update immediately and persist.
- [x] Demo namespace shows fake data end-to-end.
- [ ] DLQ multi-select and batch action bar work correctly.
- [ ] Composer sends messages with custom body and properties.
- [ ] Templates save, load, and pre-fill the composer.
- [ ] Scenario steps execute and show progress.
- [ ] Auto-refresh pauses when the tab is hidden.
- [ ] Production confirm dialog blocks accidental mutations.

## Traceability Backlinks

- `docs/features/service-bus/index.md`
- `docs/features/service-bus/technical-plan-ui-bugfixes.md`
- `docs/features/service-bus/technical-plan-backend.md`
- `docs/features/service-bus/test-plan.md`
