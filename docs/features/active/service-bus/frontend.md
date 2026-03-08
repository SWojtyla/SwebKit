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

- See `technical-plan-ui-bugfixes.md` and `technical-plan-ui-polish.md` for focused UI fixes and polish items.
