# Archive Summary — qol-improvements

---

title: "Archive Summary — qol-improvements"
owner: ""
completed_date: "2025-07-28"
pr: ""
commit: ""

---

## Goal

Catalog and deliver cross-cutting quality-of-life improvements across all SwebKit functional areas: AKS, Observability, Redis, Storage, UI Shell. Items ranged from keyboard shortcuts to pagination, binary content detection, export features, and configurable thresholds.

## Delivered

### AKS (20 of 21 items — AKS-10 skipped)

- Log search/highlight, scroll-to-bottom, filter presets, multi-pod merge
- Node filter dropdown, HPA threshold visualization, pod restart sparkline
- YAML validation before apply, quick-edit YAML with replace
- Secret base64 auto-decode, configurable CPU/Memory bar ceilings
- Port-forward start dialog, copy localhost URL
- Configurable log buffer size, auto-refresh toggle
- Better error summary for failed pods, copy name in all context menus
- Namespace search filter, pod count badges, collapsed detail pane memory

### Observability (15 of 15 items)

- Latency trend mini-chart, exception group drill-through
- Click-to-filter from table cells, export query results, saved custom queries
- KQL syntax shortcuts (Ctrl+Enter run), timezone normalization
- Configurable performance thresholds, one-click drill exception-to-trace
- Availability heatmap, resource picker dialog, auto-detect workspace vs component AI
- Copy feedback, multi-resource tab support

### Redis (12 of 12 items)

- Key scan pagination, binary content detection, sorted set score editing
- List/set/zset pagination, copy key name, key rename
- Preserve TTL across separator change, TTL dialog pre-populate
- Multi-key delete with tree checkboxes, hash field add/delete, export keys to JSON

### Storage (10 of 12 items — STG-1/2 out of scope)

- Bulk download blobs (ZIP), container-level SAS URL generation
- Copy blob relative path, blob version history listing
- Blob property detail pane, container search filter
- Lazy-load blob list per container, search/filter blobs
- Blob size display, last modified display

### UI Shell (18 of 24 items)

- Command palette: fuzzy prefix boost, "go to resource" commands, keyboard shortcuts help panel
- Grid keyboard navigation, focus restoration on modal close
- Unsaved changes detection, form validation highlighting
- Action progress in status bar, persistent notification history
- Connection string masking (PasswordField component), config export/import
- System dark/light auto-detect, color-blind safe indicators, focus rings
- Resizable left nav, collapsible sidebar sections

## Deferred / Not applicable

| Item(s)       | Reason                                                   |
| ------------- | -------------------------------------------------------- |
| REL-1 – REL-6 | ReleasesPage does not exist; pipelines-revamp superseded |
| SB-1 – SB-14  | Superseded by service-bus-ui-revamp feature              |
| AKS-10        | Low priority diff view                                   |
| STG-1, STG-2  | Blob upload/delete explicitly out of scope               |

## Key decisions

- PasswordField reusable component created in `Components/Shared/` for connection string masking across all config forms
- BinaryContentDetector uses magic-byte + non-printable-ratio heuristic (no external library)
- KQL preset sanitization uses regex replacement to prevent injection
- Timezone normalization done client-side via JS interop `getTimezoneOffset()` passed as parameter

## Lessons

- bUnit test projects that link Razor components via `<RazorComponent Include=...>` must include all transitively referenced components — missing a component causes cryptic CS1660 errors
- ApexCharts Blazor 6.x uses `PlotOptionsHeatmapColorScale` / `PlotOptionsHeatmapColorScaleRange` (not `HeatmapColorScale`)
- BlazorMonaco `KeyMod` and `KeyCode` are separate enum types; combine with `(int)` casts for bitwise OR
- `Azure.RequestFailedException` resolves as `global::Azure.RequestFailedException` when the project has a `SwebKit.Azure` namespace
