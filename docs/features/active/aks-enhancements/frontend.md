# Frontend Plan — AKS Enhancements (Batch 2)

---

title: "Frontend Plan — AKS Enhancements Batch 2"
owner: ""
status: "Done"

---

## Goal

Deliver the seven UX improvements to `AksPage.razor` and `AksPage.razor.css` without
introducing regressions in the existing panels, grids, or context menus.

## Impacted areas

- `src/SwebKit.App/Components/Pages/AksPage.razor` — primary target
- `src/SwebKit.App/Components/Pages/AksPage.razor.css` — layout and new classes
- `src/SwebKit.App/wwwroot/js/yamlHighlight.js` — YAML search interop (see backend.md)

## UX and accessibility notes

### Side-panel column

All side panels are mutually exclusive (only one content panel shows at a time, plus
events may be open simultaneously). All panels live inside `aks-panels-col`, which is
wrapped by a single `<ResizablePanel>` for drag-resize. The `ResizablePanel` is the grid
`auto` column child. Width defaults to 420px.

### Events integration

Events is a full peer `aks-panel-pane` inside the column, toggled by `ShowEvents`.
It fills the remaining column height with `flex: 1` and `overflow-y: auto` on the list.
When nothing is open (no content panel and `ShowEvents = false`), a thin vertical
`aks-events-collapsed-tab` strip appears. See Decision 002.

### YAML search

Search runs on the already-rendered `<pre>` content via JS; no Blazor re-render per
keystroke. The search toggle button sits in the panel header row. The search bar appears
below the header, above the YAML content, only when toggled on.

### Ingress URL

Each host rule renders as a `<button>` styled as a link. Single click opens the browser.
The context menu retains clipboard copy as a secondary action. `BuildIngressUrl` infers
`https://` for named hosts and `http://` for bare IP addresses.

### Pod metrics

CPU and Memory columns are always rendered. When a value is available a mini horizontal
bar (`aks-metric-bar`) is rendered below the numeric label, scaled 0–500m (CPU) and
0–512Mi (memory). The bar fills to a percentage capped at 100%. When data is absent a
`—` placeholder is shown. See Decision 005.
are always present. When a value is absent a `<span class="aks-metric-na">—</span>` is
shown, preserving column alignment.

## Tasks

### AksPage.razor (@code section)

- [x] Add `CronJobs` list and `CronJobFilter` string fields
- [x] Add `CronJobMenu` ContextMenu ref and `CtxCronJob` context field
- [x] Extend `HasAnyData`, `ActiveResourceCount`, `ActiveFilter`, `FilteredCronJobs`
- [x] Change `ShowEvents` default to `false`
- [x] Add `HasAnyPanel` property (`HasOpenPanel || ShowEvents`)
- [x] Add `_yamlViewPre` ElementReference, `_yamlSearch`, `_yamlSearchCount`, `_showYamlSearch`
- [x] Extend `LoadAsync` to call and await `GetCronJobsAsync`
- [x] Reset `CronJobs = []` in multi-namespace reset block
- [x] Extend `CloseAllMenus` to close `CronJobMenu`
- [x] Extend `OnCtxViewYaml` with `'J'` case for CronJob
- [x] Add `OnCtxOpenIngressUrl`, `BuildIngressUrl` (static), `OpenUrlAsync`
- [x] Fix `OnCtxCopyHostUrl` to use `navigator.clipboard.writeText`
- [x] Add `ShowCronJobMenu`, `OnCtxViewYamlCronJob`
- [x] Extend `CloseYaml` to reset yaml search state
- [x] Add `OnYamlSearchInput`, `ClearYamlSearch`

### AksPage.razor (template section)

- [x] Change `AutoRefreshToggle` from `Paused="@HasOpenPanel"` to `Paused="@HasAnyPanel"`
- [x] Change `aks-content` class condition from `HasOpenPanel` to `HasAnyPanel`
- [x] Replace all `ResizablePanel` slide-outs and old events panel with `aks-panels-col` structure
- [x] Add `@ref="_yamlViewPre"` to the read-only YAML `<pre>` element
- [x] Add search toggle button in YAML panel header
- [x] Add `aks-yaml-search-bar` block below YAML panel header
- [x] Change Helm history loop from `OrderBy` to `OrderByDescending`
- [x] Wrap events in `aks-events-inset` at bottom of column
- [x] Replace flat pod CPU/Mem `@if` guard with always-on columns + "—" fallback
- [x] Replace static ingress host text with `aks-ingress-url-btn` buttons
- [x] Add CronJobs grid case to the `@switch` block
- [x] Update IngressMenu to add "Open URL in browser" button
- [x] Add `CronJobMenu` context menu declaration

### AksPage.razor.css

- [x] Replace multi-variant grid rules (`events-collapsed`, `side-open.events-collapsed`) with simple `1fr` / `1fr auto` pair
- [x] Add `.aks-panels-col`, `.aks-panel-pane` flex column structure
- [x] Replace `.aks-events-panel` / `.aks-collapse-btn` with `.aks-events-inset`, `.aks-events-inset-header`, `.aks-events-chevron`
- [x] Keep `.aks-events-collapsed-tab` for the no-panel-open state
- [x] Add `.aks-yaml-search-bar`, `.aks-yaml-search-input`, `.aks-yaml-search-count`, `.aks-yaml-search-clear`
- [x] Add `.aks-search-toggle-btn` and `.active` modifier
- [x] Add `::deep .yml-search-match` highlight rule
- [x] Add `.aks-ingress-url-btn`, `.aks-ingress-hosts-cell`
- [x] Add `.aks-cron-schedule`, `.aks-suspended`, `.aks-suspended-badge`
- [x] Add `.aks-metric-na`
- [x] Add `.aks-event-warn-badge`

## Validation

- Component tests: Existing `AksPage` tests still pass (not broken by layout changes)
- Manual UX checks: see `test-plan.md`

## Notes

- `ResizablePanel` is no longer used by `AksPage`. The component itself is not removed
  (it may be used elsewhere) but none of its width-drag behaviour is needed now that the
  column has a fixed width defined in CSS.
- The `aks-sections-header` layout is reused across all panel panes without change —
  the same header markup works in the new `aks-panel-pane` context.
