# Feature Overview — Command Palette & Keyboard-First Navigation

---

title: "Command Palette & Keyboard-First Navigation"
owner: ""
status: "Planned"
created: "2026-03-21"
updated: "2026-03-21"

---

## Goal

Overhaul the command palette into a fast, comprehensive action hub and make every part of the application reachable and operable without a mouse, with discoverable shortcuts and consistent focus management.

## Value

Power users and developers expect to drive tools like SwebKit primarily from the keyboard. The current command palette is sparsely populated (navigation only) and there is no systematic focus management across grids, panels, and dialogs. A complete keyboard-first approach reduces friction for frequent workflows and makes the app feel more like a professional dev tool.

## Scope

### In scope

#### Command palette overhaul

1. **Recent commands** — Show the last 5 executed commands as a "Recent" section before the user types anything. Persisted in `UiStateRepository`.

2. **Full command coverage** — All significant actions in every feature area must be registered with the `CommandRegistry`. This includes:
   - Service Bus: Peek messages, Compose message, Resubmit selected DLQ, Refresh, Switch namespace
   - AKS: Restart deployment, Scale deployment, Open pod logs, Start port-forward, Refresh, Switch context/namespace
   - Redis: Scan keys, Refresh, Delete selected key, Set TTL
   - Storage: Refresh, Download selected blob, Copy SAS URL
   - Releases: Refresh, Trigger deployment, Open approval center
   - Global: Toggle demo mode, Open settings, Navigate to [area]

3. **Context-aware commands** — Commands that act on a selected resource (e.g. "Restart deployment") are only shown when a relevant resource is selected in the current area. Commands carry an `IsAvailable` predicate evaluated at palette-open time.

4. **Shortcut display** — Each command shows its keyboard shortcut (if any) right-aligned in the palette row. Commands without shortcuts show nothing (no placeholder).

5. **Category grouping with section headers** — Commands grouped into collapsible (or always-visible) sections: Recent, Navigation, [Current Area Actions], Global.

6. **Fuzzy search** — The search input uses fuzzy matching (not just prefix/contains) so "rb dep" can find "Restart deployment".

#### Keyboard navigation

7. **Focus trap and management in modals and panels** — All modals (`ConfirmDialog`, `Modal`), the command palette, and side panels must trap focus while open and restore it to the triggering element on close.

8. **Grid keyboard navigation** — All data grids (message list, pod grid, deployment grid, etc.) must support:
   - `↑` / `↓` to move selection
   - `Enter` to open detail panel for selected row
   - `Escape` to deselect / close detail panel
   - `Delete` to trigger the primary destructive action (with confirmation) on the selected row

9. **Tab order** — All interactive elements (buttons, inputs, dropdowns, toggles) must have a logical tab order within each page. No focus traps outside of intentional modal contexts.

10. **Shortcut discoverability** — A "Keyboard shortcuts" command in the palette (and shortcut `?`) that opens a reference sheet listing all registered shortcuts grouped by area.

11. **Skip-to-content** — Hidden "Skip to main content" link at the top of the app shell, activated by Tab on first focus, to bypass the left nav for keyboard users.

### Out of scope

- Custom shortcut rebinding by the user
- Vim-mode navigation
- Mouse gesture shortcuts
- Accessible screen reader (ARIA) compliance audit — this is a separate accessibility initiative

## Dependencies

- `CommandRegistry` — must be extended with context-awareness and availability predicates
- `CommandPalette.razor` — full rewrite of the component
- `UiStateRepository` — persist recent commands
- All feature pages — register area-specific commands on page init, update selection state
- `keyboardShortcuts.js` — may need new shortcut registrations
- All grids (Fluent `FluentDataGrid`) — keyboard event handlers
- All modals and panels — focus trap implementation (likely via JSInterop)
- `MainLayout.razor` — shortcut reference panel, skip-to-content link

## Risks

- `FluentDataGrid` keyboard events: Fluent UI's data grid component may already handle some keyboard navigation internally; custom handlers must not conflict with built-in behaviour. Audit required before implementation.
- Focus restore on panel close: reliably storing and restoring the previously focused element across Blazor re-renders requires careful use of `ElementReference` and JSInterop; see existing patterns in `CommandPalette.razor`.
- Command availability predicates: computing availability at palette-open time requires access to current component state from a singleton service. This may require components to push their selection state into a service (e.g. extend `TabService` or introduce `ISelectionContext`) rather than polling components directly.
- Shortcut conflicts: new grid shortcuts (`Enter`, `Delete`) must not fire when the user is typing in an input field; guard with `event.target.tagName !== 'INPUT'` checks in JS.

## Related documents

- Architecture: `docs/architecture/architecture.md`, `docs/architecture/design.md`
- Pitfalls: `docs/pitfalls/blazor-maui.md`
- Keyboard shortcuts JS: `src/SwebKit.App/wwwroot/js/keyboardShortcuts.js`

## Quick links

- Status: `status.md`
- Backend plan: `backend.md`
- Frontend plan: `frontend.md`
- Test plan: `test-plan.md`
