# Test Plan — Dashboard Redesign

## Validation Commands

- Build: `dotnet build src/SwebKit.App/SwebKit.App.csproj -f net10.0-windows10.0.19041.0 -p:Configuration=Debug -p:WindowsPackageType=None`
- Focused tests: `dotnet test tests/SwebKit.App.Tests --filter Dashboard` and
  `dotnet test tests/SwebKit.Core.Tests --filter UiState`
- Full suites after final wave: `tests/SwebKit.App.Tests`, `tests/SwebKit.Core.Tests`
- Aikido scan on every new/modified file per wave.

## Wave A — Decomposition (no behavior change)

| #   | Scenario                                                                         | Expectation                         |
| --- | -------------------------------------------------------------------------------- | ----------------------------------- |
| A1  | Build after each slice                                                           | 0 new warnings/errors               |
| A2  | Dashboard-filtered App tests after each slice                                    | Same pass count as before the slice |
| A3  | Manual smoke: dashboard loads, tiles refresh, customize opens, view switch works | Identical behavior to pre-slice     |

## Wave B — Tokens + restyled shared components

| #   | Scenario                                                                | Expectation                                                     |
| --- | ----------------------------------------------------------------------- | --------------------------------------------------------------- |
| B1  | Metric/watch/health tiles render with new styles in all four footprints | Correct layout at `1x1`, `2x1`, `2x2`, `3x2`                    |
| B2  | Healthy vs unhealthy tile states                                        | Severity color only on unhealthy; healthy renders quiet         |
| B3  | Per-tile loading and error states                                       | Still independent per tile; one slow tile does not block others |
| B4  | Light/dark app theme                                                    | Tokens resolve in both themes, readable contrast                |

## Wave C — Minimal default view

| #   | Scenario                                                                              | Expectation                                                                             |
| --- | ------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| C1  | Fresh install (no `ui-state.json` dashboard section)                                  | New minimal default set renders: health summary, favorites, recents                     |
| C2  | Old command-center `ui-state.json` payload (with removed/unknown tile IDs, old sizes) | Loads without crash; normalizes to valid tiles; user sees new defaults where applicable |
| C3  | Saved views from old payloads                                                         | Views preserved; tiles inside re-normalized; active view honored                        |
| C4  | Empty favorites / empty recents                                                       | Purposeful empty-state content, not blank tiles                                         |
| C5  | Demo mode                                                                             | Default tiles populate from demo clients as before                                      |

## Wave D — Builder panel redesign

| #   | Scenario                                                                                | Expectation                                                                       |
| --- | --------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------- |
| D1  | Add tile from template gallery                                                          | Tile appears in active view, persisted to `ui-state.json`                         |
| D2  | Add custom Service Bus entity watch / AKS namespace watch via inline form               | Instance tile created with `template:<instance>` ID, config persisted, data loads |
| D3  | Edit existing custom tile                                                               | Config updates persist; tile refreshes with new target                            |
| D4  | Hide / re-add / reorder / resize tiles                                                  | All actions persist per view and survive app restart                              |
| D5  | View controls: rename, duplicate, delete, reset-to-default, filters, live/snapshot flag | All work from the new panel location                                              |
| D6  | Per-view isolation                                                                      | Changes in one view do not affect other saved views                               |

## Wave E — Polish and regression

| #   | Scenario                                                                                | Expectation                                                                |
| --- | --------------------------------------------------------------------------------------- | -------------------------------------------------------------------------- |
| E1  | Full `SwebKit.App.Tests` + `SwebKit.Core.Tests` suites                                  | No new failures vs. pre-feature baseline                                   |
| E2  | Manual refresh (`RefreshRequestedEvent("dashboard")`) and periodic refresh in live mode | Unchanged behavior; snapshot mode suppresses polling                       |
| E3  | Drill-through from tiles (favorites, recents, health)                                   | Shell navigation / workspace restore paths unchanged                       |
| E4  | Window resize / narrow widths                                                           | Board reflows; compact header does not overflow                            |
| E5  | Architecture docs                                                                       | `docs/architecture/functionalities/dashboard.md` updated to the new design |
