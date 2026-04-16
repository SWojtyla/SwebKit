# Frontend Plan - visual-restyle-and-theme-overhaul

---

title: "Frontend Plan - visual-restyle-and-theme-overhaul"
owner: "GitHub Copilot"
status: "Review"

---

## Goal

Deliver a cleaner, more intentional SwebKit visual system that feels polished at the shell level and across the most table-heavy workflows, without changing the app's overall layout.

## Impacted areas

- Global design tokens and theme host:
- `src/SwebKit.App/wwwroot/app.css`
- `src/SwebKit.App/Components/Layout/MainLayout.razor`
- `src/SwebKit.App/Components/Shared/AppearanceSettings.razor`
- `src/SwebKit.Core/Configuration/UserSettingsRepository.cs`
- Prototype comparison slice:
- `src/SwebKit.App/Components/Pages/DashboardPage.razor.css`
- `src/SwebKit.App/Components/Storage/StorageBlobList.razor`
- `src/SwebKit.App/Components/Storage/StorageBlobList.razor.css`
- Shell and shared primitives:
- `src/SwebKit.App/Components/Layout/TopBar.razor`
- `src/SwebKit.App/Components/Layout/LeftNav.razor`
- `src/SwebKit.App/Components/Layout/StatusBar.razor`
- `src/SwebKit.App/Components/Shared/RoutePageHeader.razor`
- `docs/features/active/visual-restyle-and-theme-overhaul/layout.md`
- `src/SwebKit.App/Components/Shared/PageToolbar.razor`
- `src/SwebKit.App/Components/Shared/Modal.razor`
- `src/SwebKit.App/Components/Notifications/`
- Table-heavy and high-visibility feature areas:
- `src/SwebKit.App/Components/ServiceBus/`
- `src/SwebKit.App/Components/Aks/`
- `src/SwebKit.App/Components/Storage/`
- `src/SwebKit.App/Components/Pipelines/`
- `src/SwebKit.App/Components/Releases/`
- `src/SwebKit.App/Components/Redis/`
- `src/SwebKit.App/Components/Observability/`
- `src/SwebKit.App/Components/Pages/DashboardPage.razor`
- `src/SwebKit.App/Components/Pages/SettingsPage.razor`
- Current cleanup hotspots identified during planning:
- High-impact inline styling has been extracted from the table/detail surfaces that were blocking the restyle claim: AKS controls and dialogs, Storage blob list/detail panes, Service Bus message-list tooling, Releases detail/editor surfaces, and Observability logs surfaces.
- The shared table contract now relies on the richer semantic token model in `app.css` plus component-local `.razor.css` adoption for headers, row states, toolbars, detail sections, dialogs, and long-value handling.
- The remaining inline styling debt is low-signal cleanup rather than a blocker for the visual-restyle outcome.

## UX notes

- User flows: the app should feel calmer and more deliberate, with better scanability, stronger hierarchy, and more consistent surface treatment across pages.
- Global layout: keep the existing shell layout, navigation placement, and routed page composition.
- Table treatment: headers should be easier to parse, sorting/filter states should be more obvious, and selected, focused, empty, loading, and dangerous states should read consistently.
- Theme direction: use `Studio Ledger` as the high-quality dark default and evolve future palettes from that same structural language.
- Palette strategy: alternate palettes should reuse `Studio Ledger` typography, chrome, framing, and table language while varying only color tokens.
- Component states: loading, empty, and error states should remain actionable and visually consistent with shell-level styling.
- Accessibility: preserve keyboard navigation, strengthen focus visibility, and verify contrast in both dark and light themes.

## API / contract changes

- No backend contract changes are expected as part of planning.
- Small UI-state changes may be needed if implementation introduces new theme metadata, table density preferences, or shared table configuration objects.
- If shared table primitives are extracted, component parameters for sortable columns, row actions, or selection state may need to be standardized.

## Tasks

- [x] Implement the low-cost in-app pilot for `Command Deck` and `Studio Ledger` on shell chrome, Settings appearance, Dashboard surfaces, the Storage blob list, and the AKS workspace
- [x] Choose `Studio Ledger` as the global direction and retire the comparison UI from the active theme catalog
- [x] Audit current CSS tokens, inline styles, and reusable surface primitives
- [x] Remove the empty route-page header shell and replace it with a compact support-strip pattern when pages still need pills or actions
- [x] Expand the semantic token model for backgrounds, elevated surfaces, borders, focus, row states, muted text, and safety states
- [x] Rework the appearance settings experience and theme catalog presentation around `Studio Ledger` plus future palette slots
- [x] Polish shell primitives: top bar, left nav, status bar, support strips, cards, badges, dialogs, and forms
- [x] Define a shared table contract for header styling, row states, density, truncation/wrapping, sorting cues, and sticky behavior
- [x] Migrate priority table surfaces in Storage, Service Bus, AKS, Pipelines/Releases, Redis, and Observability
- [x] Remove obsolete inline styles and duplicate CSS once shared primitives exist
- [x] Add targeted component and regression tests for theme and table behavior
- [x] Update related docs if implementation changes any documented shell or settings behavior

## Layout module

- `layout.md` now owns the shell-layout rollout: top-bar context ownership, support-strip behavior, left-nav refinement, status-bar cleanup, and the palette-ready shell token pass.
- Keep `frontend.md` as the umbrella module for broader UI scope, table-system work, and feature-surface adoption.

## Validation

- Component tests: Focused restyle coverage passed (`46/46`) across AKS, Service Bus, Storage, Template Picker, and Observability seams.
- E2E build: `tests/SwebKit.E2E.Tests` builds cleanly after the theme-normalization helper updates.
- E2E smoke: the attached-session Playwright smoke run remains environment-sensitive because the fixture reuses an already-running WebView2/CDP app session that can retain stale theme state in memory.
- Manual UX checks:
- Verify theme switching and persistence
- Verify shell-level polish on Dashboard and Settings
- Verify table readability on AKS, Storage, and Service Bus first
- Verify keyboard focus and production-state cues after restyling

## Notes

- Prefer global tokens in `wwwroot/app.css` and component-local `.razor.css` files over inline `style` attributes.
- Respect `docs/pitfalls/blazor-maui.md`, especially the CSS-isolation guidance for child components and injected markup.
- Do not move child-component styles into a parent page stylesheet just to accelerate the restyle; that will create drift and broken styling later.
- The pilot is complete. Follow-on work should remove pilot-only UI and carry the selected `Studio Ledger` language into shared primitives and feature surfaces.
- Future palette work should branch from the selected `Studio Ledger` structure, not introduce a second competing shell language.
- Shell-layout specifics are tracked in `layout.md` so the remaining rollout has one concrete, reviewable execution plan.
- The implementation outcome is now broader than the original pilot plan: the high-value feature areas in scope were migrated to the shared token/table language instead of stopping at a shell-only polish pass.
