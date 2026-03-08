# Frontend Plan - Service Bus

---

title: "Frontend Plan - Service Bus"
owner: ""
status: "In Progress"

---

## Goal

Describe the UI outcome: a workspace page with a namespace panel, entity tree, tabbed message inspectors, DLQ views, batch actions, composer, and template UX.

## Impacted areas

- Files / components: `src/SwebKit.App/Components/ServiceBus/*`, `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- Pages / routes: ServiceBus page
- Shared components: `ConfirmDialog`, `TemplatePicker`, `MessageComposer`

## UX and accessibility notes

- Expose clear Active vs DLQ mode, surface `showing X of Y` when render window is partial.
- Provide keyboard navigation (arrow keys, enter, escape) and accessible focus management for message lists.
- Accessibility checks required for modal dialogs and table interactions.

## API / contract changes

- Viewmodels and DTOs: `SbEntityInfo`, `SbMessage` and grid row contracts need backward-compatible fields for DLQ/Active mode.

## Tasks

- [x] Implement namespace panel, EntityTree, and tab system
- [x] Implement DLQ multi-select and batch action bar
- [x] Implement MessageComposer and TemplatePicker
- [x] Implement auto-refresh and copy/export actions
- [ ] Implement ScenarioEditor and filter-state persistence
- [ ] Add component and manual UX tests

## Validation

- Component tests: in progress
- Manual UX checks: run smoke tests at narrow and wide window sizes

## Notes

See the UI polish and bugfix sections below for focused fixes and improvements.

## Component Hierarchy

```
ServiceBusPage (Pages/)
	namespace panel
		EntityTree (ServiceBus/)
			LoadingSpinner (Shared/)
			ErrorCallout (Shared/)
	tab area
		DlqView (ServiceBus/)
		MessageListView (ServiceBus/)
			PropRow (ServiceBus/)
		MessageDetailPane (ServiceBus/)
			PropRow (ServiceBus/)
```

## Blazor Patterns & Pitfalls

See `docs/pitfalls/blazor-maui.md` for the full reference. Entries most relevant here: BL-1 (`_Imports.razor`), BL-2 (`InvokeAsync`), BL-3 (guard before `await`), BL-4 (`@if` destroy/recreate), BL-5 (`OnParametersSetAsync` frequency).

## Focused Bug Plan

For the user-reported Service Bus UI defects (DLQ count/render mismatch, table truncation/scroll, left-panel scroll interference, and encoded topic labels), follow the focused bugfix checklist in this document and validate with component tests and manual smoke tests.

## Implementation Sequence

1. Build namespace panel with expand/collapse and add form. (Done)
2. Build `EntityTree` with queues / topics / subscriptions. (Done)
3. Build tab system and message inspector panes. (Done)
4. Add pin/unpin controls to entity rows. (Done)
5. Add demo namespace with `FakeServiceBusClient`. (Done)
6. Add DLQ multi-select with batch action bar. (Done)
7. Build message composer with property and body editors. (Done)
8. Build template picker and management sheet. (Done)
9. Build scenario editor with step list. (Pending)
10. Add auto-refresh interval selector. (Done)
11. Persist filter-state by entity path. (Pending)

## Detailed Tasks

- Add multi-select checkboxes and sticky action bar to `DlqView`.
  - Files: `src/SwebKit.App/Components/ServiceBus/DlqView.razor`
- Build `MessageComposer.razor` with body editor and property key/value table.
  - Files: `src/SwebKit.App/Components/ServiceBus/MessageComposer.razor`
- Build `TemplatePicker.razor` with save / load / delete.
  - Files: `src/SwebKit.App/Components/ServiceBus/TemplatePicker.razor`
- Build `ScenarioEditor.razor` with ordered step list and run/cancel controls.
  - Files: `src/SwebKit.App/Components/ServiceBus/ScenarioEditor.razor`
- Persist active filter state (search text, mode) keyed by entity path in `UiStateRepository`.
  - Files: `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- Add copy-to-clipboard actions to `MessageDetailPane`.
  - Files: `src/SwebKit.App/Components/ServiceBus/MessageDetailPane.razor`
- Enforce `ConfirmDialog` for mutative actions when `IsProduction` is true.
  - Files: `src/SwebKit.App/Components/ServiceBus/DlqView.razor`, `MessageComposer.razor`

## Acceptance Checks

- Namespace panel shows successful connection indicator.
- Entity tree renders queues, topics, and subscriptions correctly.
- Tabs open and close; DLQ tab is distinct from peek tab.
- Pin/unpin icons update immediately and persist.
- Demo namespace shows fake data end-to-end.
- DLQ multi-select and batch action bar work correctly.
- Composer sends messages with custom body and properties.
- Templates save, load, and pre-fill the composer.
- Scenario steps execute and show progress (pending).
- Auto-refresh pauses when the tab is hidden (visibility pause pending).
- Production confirm dialog blocks accidental mutations.

## UI Polish & UX Improvements

- Grid layout fix (headers clipped, resize handles misaligned)
- Button label clarity ("Copy JSON" → "Copy Full Message")
- Save message as template from detail pane
- Enhanced template management (rename, edit, duplicate)
- Resizable splitter between message list and detail pane
- Sortable columns, empty states, keyboard navigation, copy feedback
