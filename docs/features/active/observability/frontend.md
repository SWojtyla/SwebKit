# Frontend Plan — Observability

---

title: "Frontend Plan - Observability"
owner: ""
status: "Planned"
created: "2026-03-08"
updated: ""

---

## Goal

Deliver the observability UI: trace timeline, metrics dashboard, saved query management, OTLP config form, cross-link pre-fill, and query builder/KQL mode switch.

## Impacted areas

- `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
- `src/SwebKit.App/Components/Observability/TraceTimeline.razor`
- `src/SwebKit.App/Components/Observability/MetricsDashboard.razor`
- `src/SwebKit.App/Components/Observability/SavedQueryPanel.razor`
- `src/SwebKit.App/Components/Observability/QueryBuilder.razor`
- `src/SwebKit.App/Components/Shared/ErrorCallout.razor`
- `src/SwebKit.Core/Configuration/UiStateRepository.cs`

## Component hierarchy

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

## Blazor patterns and pitfalls

See [`docs/pitfalls/blazor-maui.md`](../../../pitfalls/blazor-maui.md) for the full reference. Most relevant here: **BL-2** (`InvokeAsync`), **BL-4** (`@if` destroy/recreate), **BL-6** (JS interop after DOM — Monaco editor).

Additional observability-specific rules:

- **Virtualize large result sets**: Use `<Virtualize>` for the log row list. Rendering thousands of `<div>` rows directly stalls the WebView renderer.
- **Chart data updates**: Pass new data via parameter updates — do not remove and re-add the chart component. Destroying and recreating an ApexCharts instance causes a visible flash.

## Implementation sequence

1. Build trace timeline UI with span hierarchy and details pane.
2. Build metrics dashboard with tile layout and refresh controls.
3. Build saved query CRUD workflow.
4. Implement `OtlpObservabilityProvider` config form.
5. Implement cross-link query pre-fill from navigation parameters.
6. Implement query builder ↔ raw KQL mode switch.
7. Add CSV and JSON export actions.

## Tasks

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

## Validation

- Component tests: Not started
- Manual checks: See `test-plan.md`

## Acceptance checks

- [ ] Trace timeline renders ordered spans with correct hierarchy.
- [ ] Metrics dashboard displays baseline tiles with refresh.
- [ ] Saved queries are created, edited, and persisted.
- [ ] Cross-links pre-fill observability filters correctly.
- [ ] Query builder and raw KQL modes both execute correctly.
- [ ] Log table remains responsive with 1 000+ rows via virtualization.
