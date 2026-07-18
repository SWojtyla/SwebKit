# UI Design — Dashboard Redesign

## Design Direction

**Calm & minimal by default, density opt-in.** The default dashboard should read like a quiet
status page: a compact header, a small health summary, and the user's own context (favorites,
recents). Everything else — KPI tiles, watch tiles, activity, open tabs — is available from the
builder panel but not shown by default.

### Visual language

- **Typography-first hierarchy**: tile values and labels carry hierarchy through size/weight, not
  boxes and borders. One display size for primary values, one label size, one meta size.
- **Muted palette**: neutral surfaces from the app theme; area colors (Service Bus, AKS, Redis,
  Pipelines) appear only as small accent cues (left edge hairline or icon tint), never as tile
  backgrounds.
- **Generous whitespace**: larger gap scale on the board grid, taller tile padding, no dense
  border chrome. Elevation via a single subtle shadow token, not borders.
- **Status severity is the only strong color**: red/amber reserved for unhealthy counts; healthy
  states render quiet (neutral text, small check cue).
- **Reduced chrome header**: replace the command-center overview strip + KPI ribbon framing with
  one compact header row: view title, view switcher, refresh state/time, customize button.

### Board model (kept)

- Existing footprints `1x1`, `2x1`, `2x2`, `3x2` and the responsive widget-board grid stay.
- Existing visual grouping (health first, workspace context next, activity lower) stays.
- Saved views with per-view filters and layout flags stay, surfaced in the compact header.

## Default View (Wave C)

| Panel                                                                                                                          | Footprint | Rationale                      |
| ------------------------------------------------------------------------------------------------------------------------------ | --------- | ------------------------------ |
| Health summary (SB dead letters, AKS unhealthy, Redis expiring, pending approvals as one calm strip or four `1x1` quiet tiles) | `1x1` ×4  | Operational signal preserved   |
| Favorites                                                                                                                      | `2x2`     | User's own context is the hero |
| Recent resources                                                                                                               | `2x2`     | Fast re-entry                  |

Everything else defaults to hidden: open tabs, activity feed, pod health alerts, KPI ribbon
variants, custom watch templates. The old default set is not migrated — new users and existing
users both get this layout (clean reset per DEC-DR-2).

Empty states matter in a minimal design: favorites/recents tiles get purposeful empty content
("Pin resources from any page" / "Recently opened resources appear here") instead of blank boxes.

## Builder Panel Redesign (Wave D)

Keep the side-panel concept, redesign the content:

1. **Template gallery** at the top — card per available tile template (icon, name, one-line
   description, area tag). Clicking adds the tile to the active view. Custom watch templates
   (Service Bus entity, AKS namespace) open their small config form inline in the card.
2. **Current layout list** below — one row per visible tile: name, footprint selector
   (`1x1`/`2x1`/`2x2`/`3x2`), up/down ordering, hide, and edit (custom tiles only).
3. **Hidden section** — collapsed list of hidden tiles with a re-add action.
4. **View controls** — rename view, duplicate, delete, reset-view-to-default; per-view filter and
   live/snapshot layout flags move here from the header dock.

No drag-and-drop (explicit non-goal). All existing builder capabilities are preserved, just
reorganized and restyled.

## Component / File Changes

| File                                                                        | Change                                                                                                |
| --------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| `src/SwebKit.App/Components/Pages/DashboardPage.razor`                      | Markup rewrite: compact header + board + builder panel; orchestration only after Wave A decomposition |
| `src/SwebKit.App/Components/Pages/DashboardPage.razor.css`                  | Rewrite on new token scale; page-level layout only                                                    |
| `src/SwebKit.App/wwwroot/css/…` or shared tokens file                       | New dashboard design tokens (spacing, type scale, accents, shadow); location decided in DEC-DR-3      |
| `src/SwebKit.App/Components/Shared/DashboardOverviewStrip.razor` (+css)     | Replaced by compact header component (new `DashboardHeader.razor`) or heavily simplified              |
| `src/SwebKit.App/Components/Shared/DashboardMetricTile.razor` (+css)        | Restyle: quiet value/label typography, accent hairline                                                |
| `src/SwebKit.App/Components/Shared/DashboardWatchTile.razor` (+css)         | Restyle to same tile language                                                                         |
| `src/SwebKit.App/Components/Shared/HealthTile.razor` (+css)                 | Restyle: severity-only color, quiet healthy state                                                     |
| `src/SwebKit.App/Components/Shared/DashboardBuilderPanel.razor` (new, +css) | Extracted + redesigned builder panel component                                                        |
| `src/SwebKit.App/Models/DashboardModels.cs`                                 | New default tile set definition; registry unchanged otherwise                                         |
| `src/SwebKit.Core/Configuration/UiStateRepository.cs`                       | Default-set change flows through existing normalization; no schema change expected                    |

## Constraints (from pitfalls / architecture docs)

- `StateHasChanged` after async work must go through `InvokeAsync` (BL-2,
  `docs/pitfalls/blazor-maui.md`).
- Keep the existing refresh architecture untouched during restyle: per-tile loading/error
  isolation, semaphore-gated `LoadHealthDataAsync`, render coalescing window, snapshot cache.
  Waves B–D are presentation-layer changes only.
- Component-local CSS isolation: no parent-page styles reaching into child tile components.
- Drill-through keeps using shell navigation + `OperatorWorkspaceService` restore paths.
- Dashboard must not render environment-readiness prompts (belongs to Settings).
