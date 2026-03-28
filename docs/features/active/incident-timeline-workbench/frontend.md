# Frontend Plan - incident-timeline-workbench

---

title: "Frontend Plan - incident-timeline-workbench"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Provide a dedicated Incident Timeline workbench page where operators can load, filter, and refresh correlated incident signals from four sources without switching pages.

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
- src/SwebKit.App/Components/IncidentTimeline/IncidentTimelineToolbar.razor
- src/SwebKit.App/Components/IncidentTimeline/IncidentTimelineList.razor
- src/SwebKit.App/Components/IncidentTimeline/IncidentTimelineRow.razor
- src/SwebKit.App/Components/IncidentTimeline/IncidentTimelineDetailPanel.razor
- src/SwebKit.App/Components/IncidentTimeline/IncidentTimelineSourceStatus.razor
- src/SwebKit.App/Components/IncidentTimeline/IncidentTimelineEmptyState.razor

## UX notes

- User flows:
- Open Incident Timeline from left navigation.
- Select time range and sources, then load timeline.
- Investigate high-severity item and inspect detail panel metadata.
- Narrow source filters and refresh to isolate likely trigger sequence.
- Component states:
- Loading: first-load skeleton and source-level loading status.
- Loaded: sorted timeline with source chip and severity styling.
- Partial: one or more source failures with warning callout and per-source status.
- Empty: no events in selected time range and source filter.
- Error: top-level failure only when all sources fail.
- Accessibility:
- Keyboard navigation for timeline row focus and detail panel open/close.
- Color is not sole signal for severity or source.
- Screen-reader labels for source health and refresh status.

## API / contract changes

- New page viewmodel state will bind to IIncidentTimelineService contract from SwebKit.Core.
- UI models should stay projection-only and avoid duplicating domain merge logic.
- Existing pages (Observability, AKS, Service Bus, Pipelines) remain unchanged and do not depend on timeline components.

## Tasks

### Wave 1 - Page shell and navigation [blazor-expert] (sequential root)

- [ ] Add new route page at /incident-timeline.
- [ ] Add navigation item in LeftNav with stable area identifier.
- [ ] Register command(s) for refresh and source quick-filter if aligned with existing command palette patterns.
- [ ] Ensure page uses existing app shell patterns for toolbar and status reporting.

### Wave 2 - Workbench components [blazor-expert] (depends on Wave 1)

- [ ] Build toolbar component for time range, source toggles, and refresh controls.
- [ ] Build timeline list and row components with source and severity badges.
- [ ] Build detail panel for selected item metadata and correlation hints.
- [ ] Build source health strip for partial-result transparency.
- [ ] Add empty, loading, and full-error states.

### Wave 3 - State, refresh, and cancellation [blazor-expert] (depends on Waves 1-2)

- [ ] Implement cancellation-first load pattern with CancellationTokenSource replacement per request.
- [ ] Ensure stale responses are ignored using request versioning.
- [ ] Implement auto-refresh with overlap prevention (single in-flight request).
- [ ] Batch render updates to avoid render thrash for large result sets.

### Wave 4 - Frontend tests and UX hardening [blazor-expert] (depends on Waves 1-3)

- [ ] Add component tests in tests/SwebKit.App.Tests for all major state transitions.
- [ ] Add interaction tests for filter changes, refresh, and detail panel behavior.
- [ ] Add regression tests to ensure no impact to existing navigation areas.
- [ ] Capture final UX tradeoffs in decisions.md if behavior differs from initial plan.

## Validation

- Component tests: Not started
- Manual UX checks:
- Verify no blank-render issues from missing namespace imports in Components/IncidentTimeline.
- Verify StateHasChanged calls after awaits are dispatched via InvokeAsync.
- Verify rapid filter changes do not trigger double-load races.

## Notes

- Follow blazor-maui pitfalls:
- Add namespace imports for new component folders in src/SwebKit.App/Components/\_Imports.razor.
- Avoid direct StateHasChanged after awaits; use InvokeAsync(StateHasChanged).
- Guard parameter-triggered loads before awaiting to avoid duplicate concurrent requests.
- Throttle high-frequency UI updates to keep the WebView responsive.
