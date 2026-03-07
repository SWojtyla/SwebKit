# Technical Plan - Observability

## Status

- Current: Pending

## Implementation Sequence

1. Implement trace timeline model mapping and UI.
2. Implement metrics dashboard with refresh and tile controls.
3. Implement saved query persistence and management.
4. Implement OTLP provider adapter in `SwebKit.OpenTelemetry`.
5. Implement cross-link contract handling from other features.
6. Implement builder mode and raw KQL mode switching.
7. Add export and robust auth failure UX.

## Detailed Tasks

- [ ] Build timeline UI with span hierarchy and details pane.
  - Files: `src/SwebKit.App/Components/Observability/TraceTimeline.razor`
- [ ] Add App Insights trace mapping logic.
  - Files: `src/SwebKit.Azure/Observability/AppInsightsObservabilityProvider.cs`
- [ ] Build metrics dashboard and tile layout.
  - Files: `src/SwebKit.App/Components/Observability/MetricsDashboard.razor`
- [ ] Add tile state persistence to UI state.
  - Files: `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- [ ] Add saved query CRUD workflow.
  - Files: `src/SwebKit.Core/Domain/*`, `src/SwebKit.App/Components/Observability/*`
- [ ] Implement OTLP provider adapter and config test path.
  - Files: `src/SwebKit.OpenTelemetry/OtlpObservabilityProvider.cs`
- [ ] Implement query parameter ingestion for cross-links.
  - Files: `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
- [ ] Implement builder-mode to KQL generation workflow.
  - Files: `src/SwebKit.App/Components/Observability/*`
- [ ] Add CSV and JSON export actions.
  - Files: `src/SwebKit.App/Components/Observability/*`
- [ ] Add targeted auth and timeout error UX.
  - Files: `src/SwebKit.App/Components/Shared/ErrorCallout.razor`, `src/SwebKit.App/Components/Observability/*`

## Acceptance Checks

- [ ] Trace timeline renders ordered spans with details.
- [ ] Metrics dashboard displays baseline tiles with refresh.
- [ ] Saved queries are created, edited, and persisted.
- [ ] OTLP provider can be configured and connection-tested.
- [ ] Cross-links pre-fill observability filters correctly.
- [ ] Query builder and raw KQL modes both execute correctly.

## Traceability Backlinks

- `docs/features/observability/index.md`
- `docs/features/observability/test-plan.md`
- `docs/plans/docs-rework-traceability/index.md`
