# Frontend Plan - incident-timeline-workbench

---

title: "Frontend Plan - incident-timeline-workbench"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Provide a dedicated incident cockpit page where operators can inspect workload-scoped evidence from four sources for one namespace and one incident window, without implying root cause.

## Impacted areas

- Existing paths likely to be touched:
- src/SwebKit.App/Components/Layout/LeftNav.razor
- src/SwebKit.App/Components/Pages
- src/SwebKit.App/Components/Routes.razor
- src/SwebKit.App/Services/CommandRegistry.cs
- src/SwebKit.App/Services/PageDataCache.cs
- src/SwebKit.App/wwwroot/app.css
- Planned new files:
- src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor
- src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor.css
- src/SwebKit.App/Components/IncidentTimeline/IncidentScopeToolbar.razor
- src/SwebKit.App/Components/IncidentTimeline/IncidentTimelineList.razor
- src/SwebKit.App/Components/IncidentTimeline/IncidentTimelineRow.razor
- src/SwebKit.App/Components/IncidentTimeline/IncidentTimelineDetailPanel.razor
- src/SwebKit.App/Components/IncidentTimeline/IncidentCoverageStrip.razor
- src/SwebKit.App/Components/IncidentTimeline/IncidentTimelineEmptyState.razor

## UX notes

- Primary user flow:
- Open Incident Timeline from left navigation.
- Select cluster context, namespace `prd-phonotif`, workload, and a bounded time window.
- Load the incident window and inspect AKS evidence first.
- Review App Insights failures, Service Bus symptoms, and recent deployment activity that appear in the same timeline.
- Open the detail panel to inspect metadata and the "linked because" explanation for each item.
- Page structure:
- Scope toolbar with namespace, workload, time-window, source toggles, and manual refresh.
- Scope summary showing the active workload and last refresh timestamp.
- Coverage strip showing loaded, failed, timed out, or unmapped sources.
- Evidence timeline ordered by UTC timestamp.
- Detail panel for raw metadata and link explanations.
- Component states:
- Loading: first-load skeleton and source-level loading status.
- Loaded: sorted evidence timeline with source chip, severity styling, and relevance label.
- Partial: one or more sources failed, timed out, or were unmapped.
- Empty: no evidence in the selected scoped window.
- Error: top-level failure only when every selected source fails.
- Accessibility:
- Keyboard navigation for timeline row focus and detail panel open or close.
- Color is not the sole signal for severity, source, or relevance.
- Screen-reader labels for source coverage, truncation, and refresh status.

## Explanation and copy rules

- The page must describe items as evidence, not causes.
- Every row should surface a short reason label such as Direct, Corroborating, or Contextual.
- The detail panel should render a plain-language explanation of why the item is included.
- The UI must not show badges or phrases such as root cause, likely cause, culprit, or inferred dependency unless future scope explicitly adds that capability.

## API / contract changes

- New page viewmodel state will bind to IIncidentTimelineService from SwebKit.Core.
- UI models should stay projection-only and avoid duplicating backend inclusion logic.
- Existing pages remain unchanged and do not depend on incident cockpit components.

## Tasks

### Wave 1 - Page shell and scope selection [blazor-expert] (sequential root)

- [ ] Add a new route page at /incident-timeline.
- [ ] Add a navigation item in LeftNav with a stable area identifier.
- [ ] Build the scope toolbar for cluster, namespace, workload, time-window, source toggles, and manual refresh.
- [ ] Ensure the page uses existing app shell patterns for toolbar and status reporting.

### Wave 2 - Evidence timeline components [blazor-expert] (depends on Wave 1)

- [ ] Build the timeline list and row components with source, severity, and relevance badges.
- [ ] Build the detail panel for selected item metadata and link explanations.
- [ ] Build the coverage strip for partial-result and unmapped-source transparency.
- [ ] Add empty, loading, truncation, and full-error states.

### Wave 3 - Request state and cancellation [blazor-expert] (depends on Waves 1-2)

- [ ] Implement cancellation-first load behavior with CancellationTokenSource replacement per request.
- [ ] Ensure stale responses are ignored using request versioning.
- [ ] Keep v1 to manual refresh only; do not add auto-refresh.
- [ ] Batch render updates to avoid render thrash for the supported windows.

### Wave 4 - Frontend tests and UX hardening [blazor-expert] (depends on Waves 1-3)

- [ ] Add component tests in tests/SwebKit.App.Tests for all major state transitions.
- [ ] Add interaction tests for scope changes, manual refresh, and detail panel behavior.
- [ ] Add regression tests to ensure no impact to existing navigation areas.
- [ ] Capture final UX tradeoffs in decisions.md if behavior differs from this plan.

## Validation

- Component tests: Not started
- Manual UX checks:
- Verify no blank-render issues from missing namespace imports in Components/IncidentTimeline.
- Verify StateHasChanged calls after awaits are dispatched via InvokeAsync.
- Verify rapid scope changes and manual refresh do not trigger stale-row flashes or double-load races.
- Verify the page explains unmapped source coverage instead of silently omitting it.

## Notes

- Follow blazor-maui pitfalls:
- Add namespace imports for new component folders in src/SwebKit.App/Components/\_Imports.razor.
- Avoid direct StateHasChanged after awaits; use InvokeAsync(StateHasChanged).
- Guard parameter-triggered loads before awaiting to avoid duplicate concurrent requests.
- Keep terminology evidence-first and avoid UI wording that suggests causal inference.
