# Technical Plan — Observability: UI

## Status

- Current: Pending

## Component Hierarchy

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

See [`docs/pitfalls/blazor-maui.md`](../../pitfalls/blazor-maui.md) for the full reference. Entries most relevant here: **BL-2** (`InvokeAsync`), **BL-4** (`@if` destroy/recreate), **BL-6** (JS interop after DOM — Monaco editor). Additional observability-specific rules:

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
  - Files: `src/SwebKit.App/Components/Observability/TraceTimeline.razor`
- [ ] Build metrics dashboard and tile layout.
  - Files: `src/SwebKit.App/Components/Observability/MetricsDashboard.razor`
- [ ] Add tile state persistence to UI state.
  - Files: `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- [ ] Add saved query CRUD workflow.
  - Files: `src/SwebKit.App/Components/Observability/SavedQueryPanel.razor`
- [ ] Implement query parameter ingestion for cross-links.
  - Files: `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
- [ ] Implement builder-mode to KQL generation workflow.
  - Files: `src/SwebKit.App/Components/Observability/QueryBuilder.razor`
- [ ] Add CSV and JSON export actions.
  - Files: `src/SwebKit.App/Components/Observability/*`
- [ ] Add targeted auth and timeout error UX.
  - Files: `src/SwebKit.App/Components/Shared/ErrorCallout.razor`
- [ ] Virtualize the log entry table.
  - Files: `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`

## Acceptance Checks

- [ ] Trace timeline renders ordered spans with correct hierarchy.
- [ ] Metrics dashboard displays baseline tiles with refresh.
- [ ] Saved queries are created, edited, and persisted.
- [ ] Cross-links pre-fill observability filters correctly.
- [ ] Query builder and raw KQL modes both execute correctly.
- [ ] Log table remains responsive with 1 000+ rows via virtualization.

## Traceability Backlinks

- `docs/features/observability/index.md`
- `docs/features/observability/technical-plan-backend.md`
- `docs/features/observability/test-plan.md`
