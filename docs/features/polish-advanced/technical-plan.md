# Technical Plan - Polish and Advanced

## Status

- Current: Pending

## Implementation Sequence

1. Complete command palette discovery and execution model.
2. Complete advanced tab management and persistence.
3. Add notification and session-history infrastructure.
4. Add import and export workflows for project config.
5. Run full keyboard shortcut audit and conflict fixes.
6. Complete theme parity for app and embedded tools.
7. Validate cross-platform behavior and credential abstractions.
8. Execute performance profiling and optimization pass.

## Detailed Tasks

- [ ] Implement fuzzy matching and ranking in command palette.
  - Files: `src/SwebKit.App/Components/Shared/CommandPalette.razor`, `src/SwebKit.App/Services/CommandRegistry.cs`
- [ ] Add recent command ranking persistence.
  - Files: `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- [ ] Implement tab drag reorder and context menu actions.
  - Files: `src/SwebKit.App/Services/TabService.cs`, `src/SwebKit.App/Components/Layout/*`
- [ ] Add tab overflow and pin protections.
  - Files: `src/SwebKit.App/Components/Layout/*`
- [ ] Add toast and notification center services.
  - Files: `src/SwebKit.App/Services/NotificationService.cs`, `src/SwebKit.App/Components/Shared/*`
- [ ] Add import and export project configuration workflows.
  - Files: `src/SwebKit.App/Components/Pages/SettingsPage.razor`, `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- [ ] Audit and fix all global keyboard shortcuts.
  - Files: `src/SwebKit.App/wwwroot/js/keyboardShortcuts.js`, `src/SwebKit.App/Components/*`
- [ ] Implement full light and dark theme propagation.
  - Files: `src/SwebKit.App/wwwroot/app.css`, `src/SwebKit.App/Components/*`
- [ ] Validate platform-specific credential and process behavior.
  - Files: `src/SwebKit.App/Platforms/*`, `docs/PLATFORM-NOTES.md`
- [ ] Run profiling scenarios and implement optimizations.
  - Files: `src/SwebKit.App/Components/*`, `src/SwebKit.Core/Services/*`

## Acceptance Checks

- [ ] Command palette performs with low latency and broad coverage.
- [ ] Tab ordering and pinning persist across sessions.
- [ ] Notification center shows actionable history.
- [ ] Config export and import preserve non-secret settings.
- [ ] Keyboard shortcuts are consistent across feature pages.
- [ ] Theme parity is consistent across UI, Monaco, xterm, and charts.
- [ ] Performance targets are met for key large-data flows.

## Traceability Backlinks

- `docs/features/polish-advanced/index.md`
- `docs/features/polish-advanced/test-plan.md`
- `docs/plans/docs-rework-traceability/index.md`
