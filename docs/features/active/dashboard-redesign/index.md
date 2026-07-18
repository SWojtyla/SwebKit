# Dashboard Redesign — Calm, Minimal, Customizable

## Goal

Replace the current "command-center" dashboard look with a fresh, calm and minimal design that is
very easy to read at a glance, while keeping the full customization model so users can add density
back through extra panels. Decompose the ~1,900-line `DashboardPage.razor` into concern-scoped
files as part of the overhaul.

**Jira:** not linked

## Quick Links

- Status: `status.md`
- Test plan: `test-plan.md`
- UI design: `frontend.md`
- Page decomposition: `decomposition.md`
- Decisions: `decisions.md`
- Architecture context: `docs/architecture/functionalities/dashboard.md`,
  `docs/architecture/design.md` (Dashboard Summary Flow)

## Scope

- **New visual language** for the dashboard: calm/minimal by default — fewer default tiles,
  generous whitespace, typography-first hierarchy, muted palette, area color as accent cue only.
- **New minimal default view**: compact status header + small set of default panels (health
  summary, favorites, recents). Density is opt-in by adding panels.
- **Improved builder panel** (no drag-and-drop): redesigned side panel with a template gallery for
  adding panels, grouped visibility toggles, per-tile size and order controls, and custom watch
  tile configuration.
- **Keep all existing capabilities**: saved views with per-view filters/layout, custom Service Bus
  entity and AKS namespace watch tiles, widget footprints (`1x1`, `2x1`, `2x2`, `3x2`), favorites,
  recents, open tabs, and activity feed panels, manual + periodic refresh, demo mode.
- **Decompose `DashboardPage.razor`** into partial class files by concern (same pattern as the
  completed `api-client-page-decomposition` feature).
- **Clean preference reset** for the new design: no migration of the visual/default layout; old
  `ui-state.json` payloads must not crash (existing normalization already safe-drops unknown
  tiles) but users start from the new default layout.

## Non-Goals

- No drag-and-drop grid interaction model.
- No new tile data providers or new integrations — data contracts of existing tiles stay as-is.
- No changes to environment profile configuration; dashboard preferences stay shell-local in
  `ui-state.json`.
- No second dashboard route; `/` and `/dashboard` remain the only entry points.
- No preference migration of layout/visual settings (clean reset accepted by user).

## Dependencies

- `UiStateRepository` dashboard preference normalization (`src/SwebKit.Core/Configuration/UiStateRepository.cs`).
- `DashboardTileRegistry` and models (`src/SwebKit.App/Models/DashboardModels.cs`).
- Shared dashboard components (`src/SwebKit.App/Components/Shared/Dashboard*.razor`, `HealthTile.razor`).
- `OperatorWorkspaceService`, `IAppEventBus`, `IPodHealthMonitorService` (unchanged consumers).

## Risks

| Risk                                                                                | Mitigation                                                                                                      |
| ----------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| Decomposition and redesign mixed in one change set makes regressions hard to bisect | Wave order: mechanical decomposition first (no behavior change), redesign after, validated per wave             |
| Old `ui-state.json` payloads break on the new default set                           | Rely on existing `NormalizeDashboardPreferences` safe-drop; add Core tests for old-payload inputs               |
| Per-tile refresh isolation regressions during restyle                               | Keep the existing render-coalescing/refresh architecture untouched in redesign waves; only presentation changes |
| Calm default hides operational signal users rely on                                 | Health summary remains in the default set; everything else is one panel-add away in the builder                 |
| CSS isolation drift between page and shared tiles                                   | Each component keeps its own scoped `.razor.css`; shared tokens go in a dedicated dashboard token stylesheet    |

## Waves

| Wave | What                                                                                     | Module             |
| ---- | ---------------------------------------------------------------------------------------- | ------------------ |
| A    | Mechanical decomposition of `DashboardPage.razor` into concern partials                  | `decomposition.md` |
| B    | Dashboard design tokens + restyled shared tile components                                | `frontend.md`      |
| C    | Minimal default view: new default tile set, calm header, clean reset                     | `frontend.md`      |
| D    | Builder panel redesign: template gallery, grouped toggles, size/order, custom tile forms | `frontend.md`      |
| E    | Polish and validation: interaction/empty/loading states, tests, docs update              | `test-plan.md`     |
