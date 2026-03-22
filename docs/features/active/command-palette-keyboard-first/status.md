# Status — Command Palette & Keyboard-First Navigation

---

title: "Status - Command Palette & Keyboard-First Navigation"
owner: ""
state: "In Progress"
branch: ""
started: "2026-03-22"
last_updated: "2026-03-22"

---

## Quick summary

Implementation complete. All feature commands registered, selection context wired, and grid keyboard navigation implemented across AKS (all resource types), Redis, and Storage. Focus trap, skip-to-content, shortcuts panel, and fuzzy command palette all in place.

## Progress checklist

- [x] Planning complete
- [x] Design reviewed
- [x] `CommandRegistry` extended (IsAvailable, AreaScope, GetAvailable, RecordUsed, RecentCommandIds)
- [x] `CommandPalette.razor` overhauled (recent, fuzzy search, categories, shortcut kbd display)
- [x] Recent commands persisted in `UiStateRepository`
- [x] `ISelectionContext` defined and implemented
- [x] Focus trap (`SwebKit.trapFocus` / `releaseTrap`) in JS
- [x] Focus trap applied to `Modal.razor`, `ConfirmDialog.razor`, `CommandPalette.razor`
- [x] `KeyboardShortcutsPanel.razor` created (`?` shortcut opens it)
- [x] Skip-to-content link in `MainLayout.razor`
- [x] `FocusFilterRequestedEvent` added to event bus
- [x] Grid keyboard navigation for all AKS resource types (↑↓/action keys/Escape)
- [x] All feature commands registered — AKS, Service Bus, Redis, Storage, Releases
- [x] Push selection to `ISelectionContext` from AKS, Service Bus, Redis, Storage pages
- [x] Grid keyboard navigation for Redis key list (↑↓/Enter/Escape on key tree panel)
- [x] Storage: Download blob + Copy SAS commands callable from palette
- [ ] Grid keyboard navigation for Storage blob list (items internal to `StorageBlobList`)
- [ ] Grid keyboard navigation for Releases board (matrix layout, not a grid)
- [ ] Tests (unit / manual)
- [ ] Docs aligned
- [ ] Ready for review

## Completed

- Backend: `AppCommand` + `CommandRegistry` extended
- Backend: `UiState.RecentCommandIds` persistence
- Backend: `ISelectionContext` + `SelectionContext` + DI registration
- Backend: `FocusFilterRequestedEvent` event type
- Frontend: `CommandPalette.razor` full rewrite — fuzzy match, section grouping, recent section, shortcut display, focus trap
- Frontend: `KeyboardShortcutsPanel.razor` — new component, lists all commands by category, focus trap
- Frontend: `keyboardShortcuts.js` — `?` shortcut, `SwebKit.trapFocus`/`releaseTrap`, `scrollFocusedCommandIntoView`
- Frontend: `MainLayout.razor` — skip-to-content, shortcuts panel toggle, `CurrentArea` passed to palette, new commands registered, `KeyboardShortcuts`/`FocusFilter` shortcut handlers
- Frontend: `Modal.razor` + `ConfirmDialog.razor` — focus trap + Escape key handling added
- Frontend: `AksPage.razor` — keyboard nav on table wrap for all resource types (↑↓/action keys/Escape); AKS commands registered; selection pushed to `ISelectionContext`
- Frontend: `ServiceBusPage.razor` — SB commands registered (Peek, Resubmit, Edit & Resubmit); message selection pushed to `ISelectionContext`
- Frontend: `RedisPage.razor` — Redis commands registered (Scan, Refresh Key, Delete Key); key list keyboard nav (↑↓/Enter/Escape); key selection pushed to `ISelectionContext`
- Frontend: `StoragePage.razor` — Storage commands registered (Refresh, Download Blob, Copy SAS); blob selection pushed to `ISelectionContext`; download/SAS implemented directly on page
- Frontend: `ReleasesPage.razor` — Releases commands registered (Refresh, New Release, Edit Release, Open Approvals)
- CSS: command palette redesigned, shortcuts panel styles, skip-link styles added

## Remaining

- Grid keyboard navigation for `StorageBlobList` (items list is internal to the component; would require exposing keyboard nav via parameter or refactor)
- Grid keyboard navigation for Releases board (matrix layout, not a navigable row list)
- Unit tests for fuzzy search, availability filtering, recent commands persistence
- Full keyboard-only walkthrough

## Blockers

None.

## Validation

Partially started.
