# Status — Dashboard Redesign

## Current State

`Review`

## Quick Summary

Complete visual overhaul of the dashboard: calm and minimal by default, customizable back to
density through an improved builder panel. Includes mechanical decomposition of the ~1,900-line
`DashboardPage.razor` into concern-scoped partial files before any visual work.

**Jira:** not linked

**Current focus:** Waves A–E implemented and validated by build + automated tests. Pending user
commit and a manual in-app smoke pass (see Validation).

## Progress Checklist

- [x] Wave A — Decompose `DashboardPage.razor` into concern partials (no behavior change)
  - [x] `DashboardPage.Preferences.cs` — views, tile preferences, persistence
  - [x] `DashboardPage.Health.cs` — health tile refresh loop, refresh gate, budgets
  - [x] `DashboardPage.CustomTiles.cs` — Service Bus entity / AKS namespace watch tiles
  - [x] `DashboardPage.Builder.cs` — customization editor state and actions
  - [x] `DashboardPage.Rendering.cs` — render state cache, coalescing, event handlers
- [x] Wave B — Dashboard design tokens + restyled shared components
  - [x] `wwwroot/styles/08-dashboard.css` shared `--dash-*` tokens (spacing, type scale, muted palette, area accents, single shadow)
  - [x] Restyled `DashboardMetricTile` (severity-only color + "All clear" cue), `DashboardWatchTile`, `HealthTile`
- [x] Wave C — Minimal default view
  - [x] New default tile set (health summary + favorites + recents) + calm compact header; ribbon/dock/overview-strip removed
  - [x] Clean reset via `DashboardPreferences.CurrentSchemaVersion`; old payloads normalize without crashing (Core tests)
- [x] Wave D — Builder panel redesign
  - [x] Template gallery (custom watch cards) + hidden-panels re-add section
  - [x] Grouped visibility/size/order controls; view controls (CRUD + filters + live/snapshot) moved into the panel
  - [x] Custom watch tile add/edit forms restyled
- [x] Wave E — Polish and validation
  - [x] Interaction, loading, and empty states (favorites/recents/activity purposeful empty content)
  - [x] Component + Core tests green, full suites no new failures (Core 724/724, App 541/541)
  - [x] `docs/architecture/functionalities/dashboard.md` updated

## Completed

- **Wave A** — `DashboardPage.razor`'s `@code` block decomposed into 5 concern-scoped partial
  classes (Preferences, Health, CustomTiles, Builder, Rendering). Pure mechanical file-boundary
  moves, no behavior change, per DEC-DR-5.
- **Wave B** — Added `wwwroot/styles/08-dashboard.css` (`--dash-*` tokens) imported via `app.css`.
  Restyled `DashboardMetricTile`, `DashboardWatchTile`, `HealthTile` to the calm language:
  typography-first hierarchy, thin area-accent hairline, single shadow token, and severity color
  only on non-zero counts (healthy zero → quiet "All clear ✓").
- **Wave C** — New minimal default set in `DashboardModels.cs` (health-summary KPIs + favorites +
  recents visible; everything else hidden). Compact header replaces the overview strip; KPI ribbon
  and insight dock removed; KPIs render as quiet `1x1` board tiles. Clean reset added to
  `UiStateRepository` via `DashboardPreferences.CurrentSchemaVersion` (=3): sub-current payloads
  load safely and reset tiles/layout to defaults while preserving saved-view id/title/filters.
- **Wave D** — Builder redesigned in place (kept inline rather than extracted — deep coupling to
  ~30 page fields and two-way-bound inputs made extraction high-risk for no benefit; frontend.md
  permitted "heavily simplified"). Three grouped sections: view controls (CRUD + filters +
  live/snapshot), template gallery (custom watch cards), current-layout list (footprint/order/
  hide/edit) + hidden-panels re-add. No drag-and-drop (DEC-DR-4).
- **Wave E** — Purposeful empty states; dead `DashboardOverviewStrip` component and now-unused
  helper methods removed; architecture doc updated.

## Blockers

_None._

## Validation

- Test Plan: `test-plan.md`
- Automated: `SwebKit.App` build clean (0 errors, 9 pre-existing warnings). Full suites green —
  **`SwebKit.Core.Tests` 724/724**, **`SwebKit.App.Tests` 541/541**. New coverage: metric-tile
  severity (healthy/attention) component tests; Core clean-reset tests (legacy payload resets tiles
  to defaults; saved views preserved with tiles reset).
- **Not yet done (needs the user):** manual in-app smoke pass (test-plan A3/C1–C5/D1–D6/E2–E4) — a
  MAUI Blazor Hybrid desktop app can't be driven headlessly in this session. Aikido SAST/secrets
  scan was not run (no Aikido tooling available in this session).

## Notes

- Clean preference reset was explicitly accepted by the user — no layout migration work.
- Deviation from plan: builder panel kept inline in `DashboardPage.razor` instead of extracted to a
  new `DashboardBuilderPanel.razor` component (see Wave D note above).
- Changes are implemented in the working tree but **not committed** — the user commits themselves.
