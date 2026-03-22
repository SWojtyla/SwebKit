# Service Bus UI Revamp — Status

**Status:** Proposed

## Current State

Planning. No code has been changed. The feature plan is written and ready for review.

## Progress Checklist

See [frontend.md](frontend.md) for the full numbered task list. Summary:

- [ ] Task 1 — CSS variables in `:root`
- [ ] Task 2 — New CSS class block in `app.css`
- [ ] Task 3 — Tab item padding override
- [ ] Task 4 — Remove JS splitter fields and lifecycle hooks from `ServiceBusPage.razor`
- [ ] Task 5 — Add `_isDetailDrawerOverlay` field and `_isDetailPaneOpen` property
- [ ] Task 6 — Restructure left panel to `sb-entity-panel` with icon-strip
- [ ] Task 7 — Restructure active workspace with conditional `ResizablePanel` / overlay drawer
- [ ] Task 8 — Add `OnMessageSelectedAsync` and `CloseDetailPane` methods
- [ ] Task 9 — Make `ToggleNamespacePane` async with localStorage persistence
- [ ] Task 10 — Add `IsDetailPaneOpen` parameter to `MessageListView`
- [ ] Task 11 — Add density field and `SetDensity` method
- [ ] Task 12 — Add density toggle button group to filter bar
- [ ] Task 13 — Apply `density-@_density` class to grid host div
- [ ] Task 14 — Restore density from localStorage on first render
- [ ] Task 15 — Add `ContentType` and `SessionId` extra columns gated on `!IsDetailPaneOpen`
- [ ] Task 16 — Pass `IsDetailPaneOpen` to `MessageListView` in page
- [ ] Task 17 — Check `ResizablePanel` CSS compatibility
- [ ] Task 18 — Verify tab padding change does not affect other pages
- [ ] Task 19 — Manual testing of full layout in all states
- [ ] Task 20 — Update `docs/architecture/functionalities/service-bus.md`

## Blockers

None.

## Notes

- The DLQ view (`DlqView.razor`) retains its existing `pane-splitter` + `SwebKitSplitter` JS splitter. That is a separate, lower-priority improvement not covered by this revamp.
- `ExpiresAt` column (task 15 comment) depends on `SbMessage.ExpiresAt` being added to the domain model. If not present, skip that column in the first iteration.
