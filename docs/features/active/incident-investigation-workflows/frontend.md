# Frontend Plan - incident-investigation-workflows

---

title: "Frontend Plan - incident-investigation-workflows"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Let operators start an investigation from the page where the triggering evidence already exists, carry that evidence into Incident Timeline, and export or review the resulting incident snapshot without retyping scope or losing context.

## Impacted areas

- Existing routes and pages:
- `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- `src/SwebKit.App/Components/Pages/PipelinesPage.razor`
- `src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor`
- `src/SwebKit.App/Components/Pages/SettingsPage.razor`
- Existing sub-components likely to host launch actions:
- `src/SwebKit.App/Components/Observability/ObservabilityFailures.razor`
- `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageDetailPane.razor`
- `src/SwebKit.App/Components/Pipelines/PipelineDetail.razor`
- `src/SwebKit.App/Components/Pipelines/PipelineActivity.razor`
- Incident Timeline components likely to change:
- `src/SwebKit.App/Components/IncidentTimeline/IncidentScopeToolbar.razor`
- `src/SwebKit.App/Components/IncidentTimeline/IncidentCoverageStrip.razor`
- `src/SwebKit.App/Components/IncidentTimeline/IncidentTimelineDetailPanel.razor`
- Planned new app-layer and UI files:
- `src/SwebKit.App/Services/IncidentInvestigationLauncher.cs`
- `src/SwebKit.App/Components/IncidentTimeline/InvestigationSeedBanner.razor`
- `src/SwebKit.App/Components/IncidentTimeline/IncidentSnapshotExportDialog.razor`
- `src/SwebKit.App/Components/IncidentTimeline/MappingProposalPanel.razor`

## UX notes

- Source-page launch flow.
- Observability should expose Investigate actions from failure, performance, or log pivots where a bounded query and evidence row already exist.
- Service Bus should expose Investigate actions from message detail and DLQ contexts where entity path, message identifiers, or correlation identifiers are visible.
- Pipelines should expose Investigate actions from run detail, deployment history, and activity rows where a deployment window is known.
- Landing behavior on `/incident-timeline`.
- The page should render an investigation-seed banner ahead of the existing toolbar summary. The banner must state where the investigation came from, what values were seeded, and what remains a draft assumption.
- The page must preserve the manual-refresh model from `incident-timeline-workbench`. A drill-through may prefill draft state and source toggles, but it should not silently requery in the background if the seed still needs operator confirmation.
- Export behavior.
- Snapshot export should be available only after at least one investigation result exists.
- Export should explain whether the bundle is full, partial, or truncated.
- Proposal behavior.
- Mapping and dependency suggestions should appear as candidate panels with explanation text and an explicit accept path. They must not look like committed topology.
- Accessibility.
- Launch actions must be keyboard reachable from lists and detail panes.
- Seed banners and proposal panels must expose source provenance and confidence text to screen readers.
- Export status must not rely on color alone.

## API / contract changes

- Introduce a frontend-facing `IncidentInvestigationSeed` projection and source provenance summary for display.
- Use an app-layer launcher service to move a seed between source pages and `IncidentTimelinePage` without baking page-specific logic into child components.
- Keep child components projection-only. The seed banner, export dialog, and proposal panel should render already-normalized models from Core or app services.
- Maintain backward compatibility with the existing `/incident-timeline` route and manual refresh behavior.

## Tasks

### Wave 1 - launch actions and landing banner [blazor-expert]

- [ ] Add launch actions to the identified Observability, Service Bus, and Pipelines surfaces.
- [ ] Implement `IncidentInvestigationLauncher` to store one transient seed and navigate to `/incident-timeline`.
- [ ] Add a landing banner on `IncidentTimelinePage` that explains seed provenance and draft assumptions.
- [ ] Preserve last-loaded evidence while a new seed is pending confirmation.

### Wave 2 - export and proposal UI [blazor-expert]

- [ ] Add snapshot export entry points and dialog states.
- [ ] Add mapping proposal and dependency-observation panels.
- [ ] Add a focused Settings handoff for accepting proposals.

### Wave 3 - later-wave watchlists and prefill automation [blazor-expert]

- [ ] Add saved watchlist UX only after seed and proposal flows are proven.
- [ ] Keep any automation as prefill-only and behind explicit operator action.

## Validation

- Component tests: Not started. Add or extend `IncidentTimelinePageTests`, `ObservabilityPageTests`, `ServiceBusPageTests`, and new launcher-specific tests.
- Manual UX checks:
- Verify every launch source preserves time range and provenance correctly.
- Verify the landing page never hides draft assumptions behind an immediate silent refresh.
- Verify export remains unavailable before data exists and clearly reports partial coverage after export.
- Verify proposal panels do not visually resemble committed settings.

## Notes

- Follow `blazor-maui.md` guardrails for new components: add namespace imports if new component folders are introduced, dispatch post-await renders via `InvokeAsync`, and avoid `OnParametersSetAsync` double-load races.
- Avoid direct page-to-page coupling. Source components should ask an app-layer launcher to navigate instead of constructing incident-page state themselves.
