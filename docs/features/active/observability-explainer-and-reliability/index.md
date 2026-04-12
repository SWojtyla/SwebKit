# Feature Overview - observability-explainer-and-reliability

---

title: "Feature Overview - observability-explainer-and-reliability"
owner: "GitHub Copilot"
status: "Planned"
jira: "not linked"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Move the Observability experience from query-first to explanation-first by adding dependency health, custom-dimension pivots, deployment before-and-after comparison, and explicit SLO tracking while preserving direct drill-through into logs and Incident Timeline.

## Value

The current Observability feature is powerful but still expects the operator to translate charts and KQL results into an explanation manually. Operators need faster answers to questions like: what changed, which dependency degraded, which dimensions are driving failures, did the latest deployment shift latency or error rate, and are we burning through our reliability budget.

This feature adds explanation layers on top of the existing provider and Logs tab. It should accelerate understanding without pretending to replace raw telemetry or declare root cause.

## Scope

- Wave 1 - explanation-first overview and pivots.
- Add explanation-oriented summary cards that tell the operator what changed and where to look next.
- Add dependency health views based on App Insights dependency telemetry.
- Add custom-dimension pivots for failures and performance so operators can quickly isolate problematic tenants, routes, queues, or role instances.
- Keep every explanation card linked to the underlying query or detailed tab.
- Wave 2 - deployment before-and-after comparison.
- Compare telemetry windows before and after an explicit deployment or release snapshot.
- Reuse `ReleaseRepository` and Azure DevOps pipeline evidence where available.
- Surface deltas, not verdicts, and keep the comparison tied to a selected deployment anchor.
- Wave 3 - SLO tracking.
- Add explicit objectives for failure rate, latency, and availability.
- Show current attainment and simple burn or risk summaries over the selected time range.
- Keep SLOs configuration-driven and transparent.
- Out of scope.
- Automatic incident declaration or root-cause inference.
- A generic BI dashboard builder or unrestricted dimension-exploration engine.
- Long-term alert delivery pipelines or external notification workflows.
- Dependency maps inferred without explicit telemetry support.

## Dependencies

- Existing Observability feature base: `docs/architecture/functionalities/observability.md`.
- Existing routes and pages: `/observability`, `/pipelines`, `/incident-timeline`, and `/settings`.
- Existing contracts and models: `IObservabilityProvider`, `ObservabilityModels.cs`, `ObservabilityConfig.cs`, `AzureAppInsightsProvider.cs`, `LogQueryResultProjector.cs`, `ReleaseRepository`, `IDevOpsClientFactory`, and `ReleaseModels.cs`.
- Cross-feature alignment: `incident-investigation-workflows` should consume drill-through actions from explanation cards; `incident-timeline-workbench` remains the downstream investigation destination.
- Relevant pitfalls: `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/agent-workflow.md`.

## Risks & mitigations

- Risk: explanation cards could overclaim what the data proves. Mitigation: every explanation must link to the underlying query or tab and avoid causal language.
- Risk: custom-dimension pivots can explode in cost or cardinality. Mitigation: explicit caps, allow-lists or top-N behavior, and clear truncation messaging.
- Risk: deployment comparisons can anchor to the wrong change. Mitigation: require an explicit deployment or release anchor and show the selected anchor in the UI.
- Risk: SLO tracking becomes opaque if it diverges from current thresholds. Mitigation: build on explicit config stored in `ObservabilityConfig` and keep the calculation model visible.

## Related documents

- Architecture map: `docs/architecture/architecture.md`
- Component design: `docs/architecture/design.md`
- Code navigation: `docs/architecture/codebase-guide.md`
- Observability functionality: `docs/architecture/functionalities/observability.md`
- Pipelines functionality: `docs/architecture/functionalities/releases.md`
- Incident Timeline functionality: `docs/architecture/functionalities/incident-timeline.md`
- Settings functionality: `docs/architecture/functionalities/settings-and-configuration.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `frontend.md`, `backend.md`, `decisions.md`
