# Frontend Plan - incident-timeline-workbench

---

title: "Frontend Plan - incident-timeline-workbench"
owner: "GitHub Copilot"
status: "Review"

---

## Goal

Provide a dedicated incident cockpit page where operators can inspect workload-scoped evidence from four sources for one namespace and one incident window, without implying root cause.

## Impacted areas

- Implemented paths:
- src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor
- src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor.css
- src/SwebKit.App/Components/Pages/IncidentTimelineConfigForm.razor
- src/SwebKit.App/Components/IncidentTimeline/
- src/SwebKit.App/Components/Pages/SettingsPage.razor
- src/SwebKit.App/Components/Layout/LeftNav.razor
- src/SwebKit.App/Components/Layout/MainLayout.razor
- src/SwebKit.App/Components/Layout/StatusBar.razor
- src/SwebKit.App/Components/\_Imports.razor
- tests/SwebKit.App.Tests/IncidentTimelinePageTests.cs
- tests/SwebKit.E2E.Tests/AppUiTests.cs

## UX notes

- Primary user flow:
- Open Incident Timeline from left navigation.
- Select cluster context, namespace `prd-phonotif`, workload, and a bounded time window.
- Load the incident window and inspect AKS evidence first.
- Review App Insights failures, Service Bus symptoms, and recent deployment activity that appear in the same timeline.
- Open the detail panel to inspect metadata and the "linked because" explanation for each item.
- Page structure:
- Scope toolbar with namespace, workload, time-window, source toggles, and manual refresh.
- Source toggles show explicit `On` / `Off` state text so operators can tell inclusion at a glance without relying on color alone.
- Scope summary showing the active workload and last refresh timestamp.
- Coverage strip showing loaded, failed, timed out, or unmapped sources.
- Mapping guidance note that points directly to Settings when selected sources are unmapped or not configured.
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
- Color is not the sole signal for source toggle state; each source chip also states `On` or `Off` explicitly.
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

- [x] Add a new route page at /incident-timeline.
- [x] Add a navigation item in LeftNav with a stable area identifier.
- [x] Build the scope toolbar for cluster, namespace, workload, time-window, source toggles, and manual refresh.
- [x] Ensure the page uses existing app shell patterns for toolbar and status reporting.

### Wave 2 - Evidence timeline components [blazor-expert] (depends on Wave 1)

- [x] Build the timeline list and row components with source, severity, and relevance badges.
- [x] Build the detail panel for selected item metadata and link explanations.
- [x] Build the coverage strip for partial-result and unmapped-source transparency.
- [x] Add empty, loading, truncation, and full-error states.

### Wave 3 - Request state and cancellation [blazor-expert] (depends on Waves 1-2)

- [x] Implement cancellation-first load behavior with CancellationTokenSource replacement per request.
- [x] Ensure stale responses are ignored using request versioning.
- [x] Keep v1 to manual refresh only; do not add auto-refresh.
- [x] Keep the current evidence visible while scope changes are pending so manual refresh remains explicit.

### Wave 4 - Frontend tests and UX hardening [blazor-expert] (depends on Waves 1-3)

- [x] Add component tests in tests/SwebKit.App.Tests for all major state transitions.
- [x] Add interaction tests for scope changes, manual refresh, and detail panel behavior.
- [x] Add regression tests to ensure no impact to existing navigation areas.
- [x] Capture the final manual-refresh UX tradeoff in decisions.md.

### Wave 5 - Mapping authoring and discoverability [blazor-expert] (post-ship UX pass)

- [x] Add Incident Timeline mapping authoring to Settings.
- [x] Deep-link the incident page into the settings section for the current scope.
- [x] Make unmapped or not-configured sources actionable instead of passive coverage labels.

## Validation

- Build: `dotnet build src/SwebKit.App/SwebKit.App.csproj -c Debug -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None` passed on 2026-04-12
- Component tests: `dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj -c Debug --filter "FullyQualifiedName~IncidentTimelinePageTests|FullyQualifiedName~IncidentTimelineConfigFormTests"` passed on 2026-04-12
- Minimal E2E: `dotnet test tests/SwebKit.E2E.Tests/SwebKit.E2E.Tests.csproj -c Debug --filter "Navigation_ToIncidentTimeline|AppShell_HasAllNavItems"` passed on 2026-04-12
- Manual UX checks:
- Verify no blank-render issues from missing namespace imports in Components/IncidentTimeline.
- Verify StateHasChanged calls after awaits are dispatched via InvokeAsync.
- Verify rapid scope changes and manual refresh do not trigger stale-row flashes or double-load races.
- Verify the page explains unmapped source coverage instead of silently omitting it.
- Verify the settings deep link opens the Incident Timeline section with the current workload scope available for mapping authoring.

## Notes

- Follow blazor-maui pitfalls:
- Add namespace imports for new component folders in src/SwebKit.App/Components/\_Imports.razor.
- Avoid direct StateHasChanged after awaits; use InvokeAsync(StateHasChanged).
- Guard parameter-triggered loads before awaiting to avoid duplicate concurrent requests.
- Keep terminology evidence-first and avoid UI wording that suggests causal inference.
- The page keeps the last loaded evidence visible while the operator edits draft scope values and surfaces a pending-refresh summary state instead of auto-querying on every change.
- The post-ship UX pass adds explicit settings-based mapping authoring instead of leaving `Unmapped` coverage as a dead end.
