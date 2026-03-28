# Frontend Plan - redis-keyspace-health-explorer

---

title: "Frontend Plan - redis-keyspace-health-explorer"
owner: ""
status: "Not started"

---

## Goal

Add an operator-focused Redis health explorer experience that surfaces keyspace risks quickly and allows direct drill-through to existing key details without introducing any mutative action in this feature.

## Impacted areas

- Pages/components:
  - src/SwebKit.App/Components/Pages/RedisPage.razor
  - src/SwebKit.App/Components/Redis/ (new health explorer component(s))
  - src/SwebKit.App/Components/Redis/RedisToolbar.razor (only if a scan trigger/filter entry point is needed)
- Styling:
  - src/SwebKit.App/Components/Redis/\*.razor.css
- Shared contracts consumed by UI:
  - src/SwebKit.Core/Models/RedisModels.cs
- Component tests:
  - tests/SwebKit.App.Tests
- E2E flows:
  - tests/SwebKit.E2E.Tests/AppUiTests.cs

## UX notes

- Primary flow:
  - User opens Redis page and runs health analysis on currently loaded keyset.
  - Explorer shows summary counts by severity and risk type.
  - User filters findings and selects an item to open existing key detail panel.
- Required component states:
  - Loading: progress text while analysis runs.
  - Loaded: findings list and prefix summary with severity badges.
  - Empty/no-risk: explicit success-neutral state.
  - Error: non-blocking error callout with retry.
  - Partial coverage: visible confidence banner using loaded/estimated key counts.
- Accessibility:
  - Keyboard navigable rows and filters.
  - Clear visual and textual severity indicators (not color-only).
- Production safety convention:
  - Explorer is read-only in this feature.
  - Any future mutative remediation CTA must use the existing ConfirmDialog typed-confirmation pattern before action.

## API / contract changes

- UI consumes RedisKeyspaceHealthReport and related finding models from Core.
- Existing Redis page selection contract remains unchanged:
  - selecting a finding should call existing key selection/refresh flow.
- Backward compatibility:
  - If optional metrics are unavailable, UI must show fallback labels instead of hiding findings silently.

## Tasks

- Wave 2 UI implementation [blazor-expert] (sequential)
  - [ ] Add Redis health explorer component(s) in src/SwebKit.App/Components/Redis/
  - [ ] Add summary cards, filters, and findings table with severity chips
  - [ ] Add click-through behavior to existing key detail panel in RedisPage
- Wave 2 async safety [blazor-expert] (parallel with component implementation)
  - [ ] Ensure async refresh uses InvokeAsync(StateHasChanged) where required (BL-2)
  - [ ] Guard state assignments before await in parameter lifecycle paths (BL-3, BL-5)
  - [ ] Keep cancellation-safe behavior when user re-scans quickly
- Wave 3 validation [blazor-expert + manual] (sequential)
  - [ ] Add component tests for all UI states and filter interactions
  - [ ] Add e2e smoke path for Redis health flow in AppUiTests
  - [ ] Validate responsive layout in Redis right-column panel stack

## Validation

- Component tests: Not started
- Manual UX checks:
  - Confirm findings are readable and sortable.
  - Confirm finding selection updates detail pane consistently.
  - Confirm partial-coverage warning appears when scan is incomplete.

## Notes

- Follow docs/pitfalls/blazor-maui.md guidance for lifecycle and render pitfalls.
- Keep visual style consistent with existing Redis panels (server info, prefix memory, key detail).
