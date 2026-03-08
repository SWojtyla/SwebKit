# Technical Plan — Polish and Advanced: UI

## Status

- Current: Pending

## Component Hierarchy

```
MainLayout
├── LeftNav               — collapse/expand, nav item active state
├── TopBar                — project selector, env buttons, command palette trigger
│   └── CommandPalette    — fuzzy search, keyboard nav
└── StatusBar             — notifications, session history
```

## Blazor Patterns & Pitfalls

See [`docs/pitfalls/blazor-maui.md`](../../pitfalls/blazor-maui.md) for the full reference. Entries most relevant here: **BL-2** (`InvokeAsync`), **BL-6** (JS interop after DOM). Polish-specific rules:

- **Theme propagation — CSS variables only**: All color, spacing, and typography values must use CSS custom properties (e.g., `var(--color-accent)`). Never hardcode color values in component inline styles. This ensures theme switching and Monaco/xterm/chart theme parity require only a root variable swap.
- **Keyboard shortcuts — register in JS, invoke via `DotNetObjectReference`**: Shortcuts are registered in `keyboardShortcuts.js`. The JS handler calls back into Blazor via `DotNetObjectReference`. Keep the shortcut registry in `CommandRegistry` so the command palette and shortcuts share the same command definitions.

## Implementation Sequence

1. Implement fuzzy matching and ranking in `CommandPalette`.
2. Implement tab drag reorder and context menu.
3. Add toast notification service and notification center.
4. Add import/export UI for project config in `SettingsPage`.
5. Audit and fix all global keyboard shortcuts.
6. Implement full light and dark theme propagation.
7. Run profiling scenarios and implement UI-level optimizations.

## Detailed Tasks

- [ ] Implement fuzzy matching and ranking in command palette.
  - Files: `src/SwebKit.App/Components/Shared/CommandPalette.razor`, `src/SwebKit.App/Services/CommandRegistry.cs`
- [ ] Implement tab drag reorder and context menu actions.
  - Files: `src/SwebKit.App/Services/TabService.cs`, `src/SwebKit.App/Components/Layout/*`
- [ ] Add tab overflow handling and pin protections.
  - Files: `src/SwebKit.App/Components/Layout/*`
- [ ] Add toast and notification center.
  - Files: `src/SwebKit.App/Services/NotificationService.cs`, `src/SwebKit.App/Components/Shared/*`
- [ ] Add import and export project configuration UI.
  - Files: `src/SwebKit.App/Components/Pages/SettingsPage.razor`
- [ ] Audit and fix all global keyboard shortcuts.
  - Files: `src/SwebKit.App/wwwroot/js/keyboardShortcuts.js`
- [ ] Implement full light and dark theme propagation.
  - Files: `src/SwebKit.App/wwwroot/app.css`
- [ ] Run profiling scenarios and implement optimizations.
  - Files: `src/SwebKit.App/Components/*`

## Acceptance Checks

- [ ] Command palette finds commands with low-latency fuzzy search.
- [ ] Tab ordering and pinning persist across sessions.
- [ ] Notification center shows actionable history.
- [ ] Config export/import preserves non-secret settings.
- [ ] Keyboard shortcuts are consistent across all feature pages.
- [ ] Theme is visually consistent across app, Monaco, xterm, and charts.
- [ ] No visible render lag on key large-data flows.

## Traceability Backlinks

- `docs/features/polish-advanced/index.md`
- `docs/features/polish-advanced/technical-plan-backend.md`
- `docs/features/polish-advanced/test-plan.md`
