# Status — Command Palette & Keyboard-First Navigation

---

title: "Status - Command Palette & Keyboard-First Navigation"
owner: ""
state: "Done"
branch: ""
started: "2026-03-22"
last_updated: "2026-03-23"

---

## Quick summary

Feature complete. All grids have keyboard navigation, command palette fully overhauled with fuzzy search and context-aware commands, focus traps in place, and unit tests passing (13 CommandRegistry tests + 16 component tests = 29 total, zero failures).

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
- [x] Grid keyboard navigation for Storage blob list (↑↓/Enter/Escape in `StorageBlobList`)
- [x] Grid keyboard navigation for Releases matrix (↑↓ row nav in `ReleaseDetail`)
- [x] Tests — 13 CommandRegistry unit tests + 16 component tests, all passing
- [x] Docs aligned
- [x] Ready for review

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
- Frontend: `StorageBlobList.razor` — keyboard nav added (↑↓/Enter/Escape on blob table)
- Frontend: `ReleaseDetail.razor` — row-level keyboard nav on release matrix (↑↓/Escape with highlight)
- Frontend: `ReleasesPage.razor` — Releases commands registered (Refresh, New Release, Edit Release, Open Approvals)
- CSS: command palette redesigned, shortcuts panel styles, skip-link styles added
- Tests: `CommandRegistryTests.cs` rewritten — 13 tests (5 legacy Search, 4 GetAvailable, 4 RecordUsedAsync)
- Tests: `ComponentTests.cs` fixed — 16 tests passing (constructor + assertion fixes)

## Remaining

_(none)_

## Blockers

None.

## Validation

All 29 tests passing (13 CommandRegistry + 16 Component). Build succeeds with zero errors.
