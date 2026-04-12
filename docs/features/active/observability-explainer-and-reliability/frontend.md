# Frontend Plan - observability-explainer-and-reliability

---

title: "Frontend Plan - observability-explainer-and-reliability"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Make the Observability page explain telemetry shifts directly before the operator drops into raw KQL, while preserving the current tabs and drill-through patterns.

## Impacted areas

- Existing page and components:
- `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
- `src/SwebKit.App/Components/Observability/ObservabilityOverview.razor`
- `src/SwebKit.App/Components/Observability/ObservabilityFailures.razor`
- `src/SwebKit.App/Components/Observability/ObservabilityPerformance.razor`
- `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor`
- `src/SwebKit.App/Components/Observability/ResourceSelectorDialog.razor`
- `src/SwebKit.App/Components/Observability/TimeRangePicker.razor`
- Existing downstream drill targets:
- `src/SwebKit.App/Components/Pages/PipelinesPage.razor`
- `src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor`
- Planned new components:
- `src/SwebKit.App/Components/Observability/ObservabilityExplainerSummary.razor`
- `src/SwebKit.App/Components/Observability/DependencyHealthPanel.razor`
- `src/SwebKit.App/Components/Observability/DimensionPivotPanel.razor`
- `src/SwebKit.App/Components/Observability/DeploymentComparisonPanel.razor`
- `src/SwebKit.App/Components/Observability/SloStatusPanel.razor`

## UX notes

- Explanation-first layout.
- The page should lead with a concise explainer summary: what changed, which dependency stands out, what deployment anchor is in play, and whether any SLO is at risk.
- Explanation cards must always offer a path to the underlying detail tab or Logs query.
- Dependency health.
- Dependency panels should show latency, failure behavior, and the selected dependency key clearly.
- The UI should distinguish top unhealthy dependencies from all observed dependencies.
- Dimension pivots.
- Pivots should be bounded and readable. Show the top contributors first and let the operator pivot into Logs or Failures for detail.
- Deployment comparison.
- The selected deployment anchor must be explicit and easy to change.
- Comparison cards should show before-and-after deltas, not present themselves as a deployment verdict.
- SLO tracking.
- SLO panels should show target, current value, and a small explanation of risk or burn.
- Accessibility.
- Summary cards and pivot tables must be keyboard reachable.
- Status or severity coloring must be paired with text labels.

## API / contract changes

- The UI should bind to higher-level explainer view models instead of assembling explanations from raw queries in Razor.
- Existing Logs and detail tabs remain intact and should not be reduced to hidden implementation details.
- Drill-through to Incident Timeline should reuse the investigation-launch pattern planned in `incident-investigation-workflows`.

## Tasks

### Wave 1 - explanation-first overview and pivots [blazor-expert]

- [ ] Add explainer summary and dependency health surfaces.
- [ ] Add bounded custom-dimension pivot panels.
- [ ] Add direct links from cards to existing detail tabs and logs.

### Wave 2 - deployment comparison [blazor-expert]

- [ ] Add anchor selection and before-or-after comparison views.
- [ ] Surface pipeline or release context without forcing the operator into the Pipelines page.

### Wave 3 - SLO tracking [blazor-expert]

- [ ] Add SLO panels and state badges.
- [ ] Keep the UI explicit about selected targets and time windows.

## Validation

- Component tests: Not started. Extend current Observability bUnit suites and add focused coverage for new summary and pivot components.
- Manual UX checks:
- Verify the page remains understandable without opening Logs first.
- Verify every explanation card still offers a path to the underlying data.
- Verify deployment comparison and SLO panels do not crowd out the existing tabs.

## Notes

- Follow `blazor-maui.md` guidance for any new child components and ensure current render-guard patterns remain intact when the active resource or time range changes.
- Keep global styling consistent with the existing Observability section in `wwwroot/app.css` unless a new isolated component needs its own `.razor.css` file.
