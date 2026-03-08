# Frontend Plan - Observability

---

title: "Frontend Plan - Observability"
owner: ""
status: "Pending"

---

## Goal

Describe the UI/client outcome and user-facing changes.

## Impacted areas

- Files / components: `src/...`
- Pages / routes
- Shared components

## UX and accessibility notes

- Expected user flows and states
- Accessibility considerations (a11y checks required)

## API / contract changes

- DTOs, props, events, and contracts that will change
- Backward compatibility notes

## Tasks

- [ ] Update UI contract / viewmodel
- [ ] Implement components / pages
- [ ] Handle loading, error, and empty states
- [ ] Wire to backend / state layer
- [ ] Add unit / component tests
- [ ] Add e2e tests for core flows
- [ ] Accessibility review

## Validation

- Component tests: Not started / In progress / Passed
- Manual UX checks: list of acceptance steps

## Notes

- Important implementation details, style guide references, or design tokens

---

## (Source content preserved)

```
ObservabilityPage (Pages/)
├── log table + detail pane
│   └── PropRow (ServiceBus/)
├── TraceTimeline (Observability/)
├── MetricsDashboard (Observability/)
│   └── ApexCharts tiles
└── query bar
    ├── KQL editor (BlazorMonaco)
    └── query builder controls
```

## Blazor Patterns & Pitfalls

See [`docs/pitfalls/blazor-maui.md`](../../../pitfalls/blazor-maui.md) for the full reference. Entries most relevant here: **BL-2** (`InvokeAsync`), **BL-4** (`@if` destroy/recreate), **BL-6** (JS interop after DOM — Monaco editor). Additional observability-specific rules:

- **Virtualize large result sets**: Use `<Virtualize>` for the log row list. Rendering thousands of `<div>` rows directly stalls the WebView renderer.
- **Chart data updates**: Pass new data via parameter updates — do not remove and re-add the chart component. Destroying and recreating an ApexCharts instance causes a visible flash.

## Implementation Sequence

1. Build trace timeline UI with span hierarchy and details pane.
2. Build metrics dashboard with tile layout and refresh controls.
3. Build saved query CRUD workflow.
4. Implement `OtlpObservabilityProvider` config form.
5. Implement cross-link query pre-fill from navigation parameters.
6. Implement query builder ↔ raw KQL mode switch.
7. Add CSV and JSON export actions.

## Detailed Tasks

- [ ] Build timeline UI with span hierarchy and details pane.
- [ ] Build metrics dashboard and tile layout.
- [ ] Add tile state persistence to UI state.
- [ ] Add saved query CRUD workflow.
- [ ] Implement query parameter ingestion for cross-links.
- [ ] Implement builder-mode to KQL generation workflow.
- [ ] Add CSV and JSON export actions.
- [ ] Add targeted auth and timeout error UX.
- [ ] Virtualize the log entry table.

## Acceptance Checks

- [ ] Trace timeline renders ordered spans with correct hierarchy.
- [ ] Metrics dashboard displays baseline tiles with refresh.
- [ ] Saved queries are created, edited, and persisted.
- [ ] Cross-links pre-fill observability filters correctly.
- [ ] Query builder and raw KQL modes both execute correctly.
- [ ] Log table remains responsive with 1 000+ rows via virtualization.
