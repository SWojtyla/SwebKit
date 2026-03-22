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

Implementation in progress. Core backend and primary UI components complete. Grid nav partially done (Deployments). Focus trap, skip-to-content, shortcuts panel, and fuzzy command palette all implemented.

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
- [x] Grid keyboard navigation for AKS Deployments (↑↓/Escape)
- [ ] All feature commands registered (Service Bus, AKS, Redis, Storage, Releases) — partial
- [ ] Grid keyboard navigation for remaining AKS resource types (StatefulSets, Pods, etc.)
- [ ] Grid keyboard navigation for Redis key list
- [ ] Grid keyboard navigation for Storage blob list
- [ ] Grid keyboard navigation for Releases board
- [ ] Push selection to `ISelectionContext` from feature pages
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
- Frontend: `AksPage.razor` — keyboard nav on table wrap for Deployments (↑↓/Escape)
- CSS: command palette redesigned, shortcuts panel styles, skip-link styles added

## Remaining

- Register area-specific commands in Service Bus, AKS, Redis, Storage, Releases pages
- Grid keyboard navigation for other AKS resource types (Pods, StatefulSets, etc.)
- Keyboard nav for Redis key list, Storage blob list, Releases board
- Push `SelectedDeployment` / selected pod etc. to `ISelectionContext`
- Unit tests for fuzzy search, availability filtering, recent commands persistence
- Full keyboard-only walkthrough

## Blockers

None.

## Validation

Partially started.
