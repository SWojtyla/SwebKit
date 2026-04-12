# Feature Overview - incident-investigation-workflows

---

title: "Feature Overview - incident-investigation-workflows"
owner: "GitHub Copilot"
status: "Planned"
jira: "not linked"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Turn Incident Timeline into the shared investigation target for the rest of SwebKit so an operator can launch an evidence-backed investigation from Observability, Service Bus, or Pipelines without re-entering scope or losing the triggering context.

## Value

`incident-timeline-workbench` already delivers a workload-scoped evidence page, but it still behaves as a destination the operator must reach manually. The triggering context often starts elsewhere: a failed dependency in Observability, a dead-lettered message in Service Bus, or a suspicious deployment in Pipelines. Today that handoff is manual, repetitive, and easy to distort by memory.

This follow-on feature keeps the investigation evidence-first. It carries forward the triggering signal, the selected time window, and any existing correlation identifiers so the operator starts from a defensible investigation seed. It must not imply that SwebKit has determined root cause.

## Scope

- Wave 1 - investigation launch and evidence continuity.
- Add drill-through actions from `/observability`, `/service-bus`, and `/pipelines` into `/incident-timeline`.
- Carry forward the current time range, source area, selected failure or message or run, correlation identifiers, and any explicit workload mapping already present in `AppConfig.IncidentTimeline.WorkloadMappings`.
- Show a landing summary on `IncidentTimelinePage` that explains what was seeded, what was inferred from existing config, and what still needs operator confirmation before refresh.
- Add bounded incident snapshot export for the current timeline result as JSON and markdown, with explicit source coverage and redaction rules.
- Wave 2 - mapping discovery proposals and dependency-map groundwork.
- Surface candidate workload mappings and dependency observations based on already-loaded evidence and existing configuration.
- Treat proposed mappings as advisory only. The feature may prefill Settings or a review panel, but it must not persist mappings automatically.
- Capture dependency observations that can later support dependency health overlays in Observability and saved investigation watchlists.
- Wave 3 - later wave and intentionally deferred until Waves 1 and 2 prove value.
- Saved watchlists for named workloads, queue or dependency hotspots, or recurring incident signatures.
- Light automation such as prefilled investigation seeds from recent alerts or pipeline failures.
- Out of scope.
- Automated root-cause detection, culprit ranking, or language that implies the product discovered causation.
- Auto-accepting workload mappings, dependency edges, or source ownership.
- Background auto-refresh or always-on monitoring for early waves.
- Remediation actions from the investigation surface.
- Long-term incident case management or evidence storage outside the current export bundle.

## Dependencies

- Active dependency: `docs/features/active/incident-timeline-workbench/` remains the prior foundation and stays in `Review`.
- Existing routes: `/incident-timeline`, `/observability`, `/service-bus`, `/pipelines`, `/settings`.
- Existing contracts and services: `IIncidentTimelineService`, `IncidentTimelineService`, `IncidentTimelineConfig`, `IObservabilityProvider`, `IServiceBusClient`, `IDevOpsClient`, and `ReleaseRepository`.
- Cross-feature alignment: `observability-explainer-and-reliability` should supply richer dependency-health and deployment-comparison inputs; `service-bus-operator-workbench` should supply stronger message-trace pivots and DLQ evidence.
- Relevant architecture docs: `docs/architecture/functionalities/incident-timeline.md`, `docs/architecture/functionalities/observability.md`, `docs/architecture/functionalities/service-bus.md`, `docs/architecture/functionalities/releases.md`, `docs/architecture/functionalities/settings-and-configuration.md`.
- Relevant pitfalls: `docs/pitfalls/agent-workflow.md`, `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/dotnet-csharp.md`.

## Risks & mitigations

- Risk: drill-through launches could overstate certainty if the landing page looks like a pre-solved incident. Mitigation: the landing state must show seed provenance, unresolved assumptions, and explicit operator confirmation requirements.
- Risk: correlation IDs could broaden the search into unrelated workloads. Mitigation: correlation passthrough may narrow or explain evidence inside the selected scope, but it must not bypass workload-scoped inclusion rules from `incident-timeline-workbench`.
- Risk: snapshot export could leak payloads, secrets, or oversized raw blobs. Mitigation: export a sanitized bundle with explicit field allow-lists and truncation markers.
- Risk: mapping discovery could drift into silent ownership inference. Mitigation: all mapping and dependency suggestions are proposals with explanation text and an explicit accept path.
- Risk: later-wave watchlists or automation could become a hidden background agent. Mitigation: keep early waves manual and store later-wave automation goals in `decisions.md` as constrained follow-up scope only.

## Related documents

- Architecture map: `docs/architecture/architecture.md`
- Component design: `docs/architecture/design.md`
- Code navigation: `docs/architecture/codebase-guide.md`
- Incident Timeline functionality: `docs/architecture/functionalities/incident-timeline.md`
- Observability functionality: `docs/architecture/functionalities/observability.md`
- Service Bus functionality: `docs/architecture/functionalities/service-bus.md`
- Pipelines functionality: `docs/architecture/functionalities/releases.md`
- Settings functionality: `docs/architecture/functionalities/settings-and-configuration.md`
- Active dependency: `docs/features/active/incident-timeline-workbench/`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `frontend.md`, `backend.md`, `decisions.md`
