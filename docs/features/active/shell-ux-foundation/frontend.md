# Frontend Plan - shell-ux-foundation

---

title: "Frontend Plan - shell-ux-foundation"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Give SwebKit one coherent shell and page-chrome language so that operators always know where they are, what environment they are in, what state the page is in, and what the next safe action is.

## Impacted areas

- Core shell files:
- `src/SwebKit.App/Components/Layout/MainLayout.razor`
- `src/SwebKit.App/Components/Layout/MainLayout.razor.css`
- `src/SwebKit.App/Components/Layout/LeftNav.razor`
- `src/SwebKit.App/Components/Layout/NavItem.razor`
- `src/SwebKit.App/Components/Layout/TopBar.razor`
- `src/SwebKit.App/Components/Layout/StatusBar.razor`
- `src/SwebKit.App/Components/Routes.razor`
- Shared primitives likely to change or be extended:
- `src/SwebKit.App/Components/Shared/PageToolbar.razor`
- `src/SwebKit.App/Components/Shared/EmptyState.razor`
- `src/SwebKit.App/Components/Shared/LoadingContainer.razor`
- `src/SwebKit.App/Components/Shared/ErrorCallout.razor`
- `src/SwebKit.App/Components/Shared/AppearanceSettings.razor`
- Notification surfaces:
- `src/SwebKit.App/Components/Notifications/NotificationHistory.razor`
- `src/SwebKit.App/Components/Notifications/NotificationToast.razor`
- Routed pages that currently use inconsistent heading or toolbar patterns:
- `src/SwebKit.App/Components/Pages/DashboardPage.razor`
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- `src/SwebKit.App/Components/Pages/AksPage.razor`
- `src/SwebKit.App/Components/Pages/RedisPage.razor`
- `src/SwebKit.App/Components/Pages/StoragePage.razor`
- `src/SwebKit.App/Components/Pages/PipelinesPage.razor`
- `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
- `src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor`
- `src/SwebKit.App/Components/Pages/SettingsPage.razor`
- Tests:
- `tests/SwebKit.App.Tests/ComponentTests.cs`
- `tests/SwebKit.App.Tests/NotificationServiceTests.cs`
- `tests/SwebKit.E2E.Tests/AppUiTests.cs`

## UX notes

- Current shell inconsistency to fix:
- `DashboardPage` uses its own bespoke header.
- `ObservabilityPage` already uses `PageToolbar`.
- `PipelinesPage` still renders `h2` instead of a top-level `h1`.
- `SettingsPage` renders section-local `h3` titles instead of a route-level page header.
- `Routes.razor` currently uses `FocusOnNavigate` with `Selector="h1"`, so page-heading inconsistency is not only visual - it affects focus behavior.
- Target user flow:
- Enter any route directly and still see the correct active nav state, page header, environment context, and refresh/status semantics.
- Hit an empty or unconfigured page and immediately get a CTA that explains what to do next.
- Read notification severity, unread state, and timestamps without having to infer meaning from ad hoc markup.
- Distinguish production vs non-production context before triggering destructive actions.
- Accessibility expectations:
- Maintain keyboard access to nav, command palette button, notifications, and page actions.
- Preserve a stable `h1` for each route so `FocusOnNavigate` lands correctly.
- Avoid color-only communication for production context, status, and notification severity.

## API / contract changes

- Introduce a shell context model for routed pages, with fields such as area key, title, subtitle, section group, and optional route-level actions.
- Derive active nav state from route metadata or `NavigationManager` location, not from click handlers alone.
- Standardize a shared page-header contract for top-level routes so shell focus, context, and status behavior are predictable.
- Keep notification history backward compatible with the current persisted model while allowing improved read/unread or grouping behavior.

## Tasks

### Wave 1 - Route-aware shell context [blazor-expert] (sequential root)

- [ ] Replace purely imperative `CurrentArea` tracking with route-derived shell context.
- [ ] Group left-nav items by operator intent rather than a flat list.
- [ ] Extend top-bar context to show the current area, active environment, and any shell-level safety state.
- [ ] Preserve existing refresh and shortcut behavior while moving shell state derivation closer to routing.

### Wave 2 - Shared page-header and state patterns [blazor-expert] (depends on Wave 1)

- [ ] Introduce one shared top-level page-header pattern that all routed pages can adopt.
- [ ] Normalize `h1` usage across routed pages so shell focus works consistently.
- [ ] Roll out one structural pattern for loading, error, and empty states with CTA placement.
- [ ] Update pages that currently use bespoke header stacks or passive empty text.

### Wave 3 - Status, notifications, and refresh trust [blazor-expert] (depends on Waves 1-2)

- [ ] Align status-bar refresh language with page refresh behavior.
- [ ] Improve notification-center presentation, unread behavior, and severity readability.
- [ ] Reduce repeated inline top-bar and notification styling in favor of maintainable shell styles.
- [ ] Ensure connection/status signals do not imply success when the page is unconfigured or partially failed.

### Wave 4 - Theme and production-safety polish [blazor-expert] (depends on Waves 1-3)

- [ ] Audit shell colors, tokens, and theme persistence behavior.
- [ ] Surface environment production context consistently at shell level.
- [ ] Standardize shared destructive-action emphasis so pages do not each invent their own production treatment.
- [ ] Record the final shell-context and safety model in `decisions.md`.

### Wave 5 - Validation and rollout [blazor-expert] (depends on Waves 1-4)

- [ ] Add component coverage for shell context, route state, notifications, and shared page headers.
- [ ] Add E2E coverage for direct-route entry, shell nav behavior, and theme persistence.
- [ ] Verify the updated shell pattern across all core routed pages before declaring the foundation stable.

## Validation

- Component tests: Not started.
- Manual UX checks:
- Open each top-level route directly and verify correct nav state, page header, and focus target.
- Toggle between normal and production-marked environments and verify shell safety treatment.
- Trigger notification flows and confirm toast/history consistency.
- Change theme, restart the app, and verify consistent shell rendering.

## Notes

- Relevant pitfalls from `docs/pitfalls/blazor-maui.md`:
- BL-2 - dispatch UI updates via `InvokeAsync(StateHasChanged)` after awaits.
- BL-5 - guard `OnParametersSetAsync` work so shell/page context does not reload on every parent render.
- BL-11 - keep child-component CSS in child `.razor.css` files and shared shell tokens in global styles.
- Relevant pitfalls from `docs/pitfalls/dotnet-csharp.md`:
- CS-2 - do not swallow `OperationCanceledException` in shell refresh or notification flows.
