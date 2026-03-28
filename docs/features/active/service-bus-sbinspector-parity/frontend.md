# Frontend Plan - service-bus-sbinspector-parity

---

title: "Frontend Plan - service-bus-sbinspector-parity"
owner: "Unassigned"
status: "Planned"

---

## Goal

Deliver SBInspector-level Service Bus capabilities in SwebKit through consistent, approachable workflows that maintain existing production safety cues, accessibility, and keyboard behavior, with scope focused on functional parity and operational capability.

## Impacted areas

- Primary page and Service Bus components:
  - `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
  - `src/SwebKit.App/Components/ServiceBus/EntityTree.razor`
  - `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`
  - `src/SwebKit.App/Components/ServiceBus/MessageComposer.razor`
  - `src/SwebKit.App/Components/ServiceBus/DlqView.razor`
  - `src/SwebKit.App/Components/ServiceBus/ScheduledMessages.razor`
- Potential shared UI touchpoints (as required by implementation):
  - `src/SwebKit.App/Components/Shared/`
  - `src/SwebKit.App/wwwroot/app.css`
- Tests:
  - `tests/SwebKit.App.Tests/`
  - `tests/SwebKit.E2E.Tests/`

## UX notes

- User flows:
  - Entity administration flow: select entity -> enable/disable -> confirm -> immediate status feedback.
  - Message management flow: select row(s) -> delete/purge/filter action -> confirmation -> auto-refresh.
  - Power-user data triage flow: compose filters -> customize columns -> page through results -> export (JSON)/delete subset.
  - Template flow: create template in composer -> reuse with edits -> send or schedule.
- Component states:
  - Every new panel/action must handle loading, loaded, empty, and error states.
  - Destructive action states must provide clear disabled/loading behavior to prevent duplicate execution.
- Accessibility and keyboard:
  - Preserve existing navigation semantics across tree, list, filters, and composer.
  - Ensure confirmation and configuration dialogs are keyboard-operable and focus-safe.
  - Keep production warning cues visually strong and screen-reader discoverable.

Pitfall guardrails that must be applied in implementation:

- Use `InvokeAsync(StateHasChanged)` where async lifecycle updates risk stale UI (BL-2).
- Guard `OnParametersSetAsync` against duplicate concurrent loads (BL-3/BL-5).
- Avoid state loss from unintended `@if` destroy/recreate behavior for durable UI state (BL-4).
- Keep CSS isolation boundaries correct, including `::deep` only where truly needed (BL-9/BL-11).

## API / contract changes

- New/updated frontend contracts expected:
  - View-model support for entity status operations and destructive-action result feedback.
  - Filter state model with multi-field operators and persistent definitions.
  - Column configuration model including custom-property columns.
  - Pagination state model (current page, continuation, load-more availability).
  - Template list/detail model for message composer.

Backward compatibility notes:

- Existing Service Bus tabs and workflows should remain usable while wave features roll out incrementally.
- New UX elements should integrate into existing layout patterns instead of introducing disconnected interaction models.

## Tasks

### Wave 1 - Critical entity/message management UI [blazor-expert] (depends on backend Wave 1 contracts)

- [ ] Add entity enable/disable actions and status indicators in entity views
- [ ] Add single-message delete entry points in message and DLQ lists
- [ ] Add purge-all action with production-safe confirmation UX
- [ ] Add post-operation auto-refresh and clear operation feedback
- [ ] Add component tests for success/failure/permission-denied states

### Wave 2 - Advanced filtering UI [blazor-expert] (depends on backend Wave 2 contracts)

- [ ] Add filter builder UI for multi-field operators and logical composition
- [ ] Add filter persistence and on/off toggle behavior
- [ ] Add delete filtered and export filtered (JSON) command UX
- [ ] Capture CSV export as deferred follow-up scope (no implementation in this feature)
- [ ] Add confirmation and result summaries for filtered actions
- [ ] Add tests for filter state transitions and action enablement rules

### Wave 3 - Column customization and density [blazor-expert] (parallel with backend Wave 3 persistence)

- [ ] Add column chooser for built-in and custom-property columns
- [ ] Add row density controls aligned with existing SwebKit patterns
- [ ] Persist and restore view preferences
- [ ] Add tests for persistence, reset behavior, and keyboard navigation

### Wave 4 - Pagination/load-more UI [blazor-expert] (depends on backend Wave 4 contracts)

- [ ] Add load-more controls and page status indicators
- [ ] Preserve filter and selection context as pages load
- [ ] Ensure responsive rendering in high-volume lists
- [ ] Add tests for paging edge cases and continuity

### Wave 5 - Message templates UI [blazor-expert] (depends on backend Wave 5 persistence)

- [ ] Add template create/edit/delete/apply UX in composer
- [ ] Add template list/search/select interactions where needed
- [ ] Add validation and error feedback for invalid template data
- [ ] Add tests for template lifecycle and compose/send integration

### Documentation and decision hygiene [manual] (ongoing)

- [ ] Keep `decisions.md` updated for major UX tradeoffs
- [ ] When behavior changes, update `docs/architecture/functionalities/service-bus.md` in the same change set

## Validation

- Component tests: Not started
- Manual UX checks:
  - Keyboard-only execution of destructive and non-destructive flows
  - Production safety cue visibility and confirmation behavior
  - Filter/paging/column persistence behavior across reloads

## Notes

- This plan prioritizes capability parity without sacrificing SwebKit's established usability model.
- Settings/theming parity and CSV export are intentionally deferred beyond this feature scope.
- If any parity request conflicts with SwebKit interaction consistency, capture the tradeoff in `decisions.md` before implementation.
