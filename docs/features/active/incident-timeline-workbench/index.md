# Feature Overview - incident-timeline-workbench

---

title: "Feature Overview - incident-timeline-workbench"
owner: "GitHub Copilot"
status: "Planned"
jira: "not linked"
created: "2026-03-28"
updated: "2026-04-11"

---

## Goal

Deliver a workload-scoped incident cockpit for one workload, one namespace, and one incident window. The workbench should assemble time-ordered evidence from AKS, App Insights, Service Bus, and recent deployment or release activity so an operator can inspect what happened around an incident without switching across multiple pages.

## Value

Today incident investigation is fragmented across Observability, AKS, Service Bus, and release views. Operators manually compare timestamps and resource names, which slows triage and encourages overconfident cause assumptions.

V1 should optimize for a concrete operational question: a pod is down in namespace `prd-phonotif`; what related AKS events, App Insights failures, Service Bus symptoms, and recent deployment activity occurred in the same investigation window?

This feature is evidence-first. It helps operators inspect adjacent signals and understand why each item is shown. It does not claim to detect or rank root cause.

## Scope

- In scope:
- One active investigation scope at a time: profile, cluster context, namespace, workload selector, and bounded incident window.
- Read-only incident evidence timeline with explicit source coverage and per-item link explanation.
- New cross-source contracts in SwebKit.Core for workload scope, normalized evidence items, and link explanations.
- Additive source adapters for AKS, App Insights, Service Bus, and DevOps release or deployment activity.
- New incident cockpit page in SwebKit.App.
- Out of scope:
- Automatic root-cause detection, causal scoring, culprit ranking, or language that implies causation.
- Cross-workload or cross-namespace dashboards.
- Auto-discovery of unknown ownership mappings across the wider estate.
- Remediation actions from timeline items.
- Persistent incident timelines or long-term evidence storage.
- Cross-tenant or cross-profile investigation in one query.

## Workload scope definition

A v1 query is anchored to one workload investigation scope:

- Environment or profile context.
- AKS cluster or connection context.
- Namespace, for example `prd-phonotif`.
- Workload selector: deployment, stateful set, daemon set, pod owner chain, or a supported label mapping.
- Incident window: one bounded shared time range, defaulting to a short recent window.

AKS is the anchor source for the initial scenario. Other sources are included only when they can be legitimately linked back to the scoped workload.

## Linking semantics

V1 uses explicit linking semantics instead of generic "correlation" claims:

- Ownership or topology match: the source item maps to the scoped workload or one of its known dependencies.
- Time-window proximity: the item happened inside the selected investigation window.
- Existing correlation ID: a request, operation, message, or trace identifier already present in the underlying systems connects the evidence.

These links explain why evidence is shown. They do not explain why the incident happened.

## Inclusion rules

- AKS: include pod lifecycle changes, restarts, warnings, scheduling failures, and namespace or workload events that resolve to the selected workload or owner chain.
- App Insights: include failures, exceptions, and selected request or dependency evidence only when the scoped workload maps to the configured app or component, or an existing correlation ID ties the record to the scoped workload.
- Service Bus: include queue, topic, or subscription symptoms only when the entity is already mapped to the scoped workload, its known messaging topology, or an existing correlation ID.
- Deployments or releases: include rollout, deployment, or release activity only when it targets the same app, environment, or namespace and falls inside the investigation window.
- Time-window proximity alone does not pull in unrelated workloads. It is contextual evidence inside the scoped candidate set, not a cross-estate discovery mechanism.

## Confidence and explanation model

Each evidence item must explain "linked because ...". V1 should use a simple relevance model:

- Direct: explicit workload ownership or topology match.
- Corroborating: existing correlation ID, or explicit app or entity mapping plus time overlap.
- Contextual: time-window match for already-scoped deployment or platform activity.

These labels describe inclusion confidence and operator usefulness. They must never be rendered as causal confidence or root-cause probability.

## Safer MVP cut

- Single-scope investigation only.
- Read-only timeline and detail panel.
- Manual refresh only in v1.
- Bounded incident windows such as 15 minutes, 1 hour, and 6 hours.
- Hard result caps with transparent truncation messaging instead of deep paging in v1.
- If a source cannot be mapped safely to the scoped workload, omit it and show that coverage is unavailable or unmapped.

## Dependencies

- Internal projects and paths:
- src/SwebKit.App for route, page shell, scope selector, timeline components, and page-level orchestration.
- src/SwebKit.Core for scope models, evidence contracts, link explanation models, and aggregation orchestration.
- src/SwebKit.Observability for an App Insights evidence adapter using the existing provider query surface.
- src/SwebKit.Kubernetes for an AKS evidence adapter based on events and pod lifecycle metadata.
- src/SwebKit.Azure for a Service Bus evidence adapter.
- src/SwebKit.DevOps for deployment or release activity adapters from existing run or release metadata.
- Architecture constraints that must remain true during implementation:
- Cross-source contracts live in SwebKit.Core.
- Existing source interfaces remain additive.
- New UI entry point lives in SwebKit.App.
- Architecture and functional docs expected to be updated during implementation:
- docs/architecture/functionalities/observability.md
- docs/architecture/functionalities/aks.md
- docs/architecture/functionalities/service-bus.md
- docs/architecture/functionalities/releases.md

## Risks & mitigations

- Risk: generic "correlation" wording encourages overclaiming and wrong operator expectations.
- Mitigation: use explicit link explanations and ban causal copy in contracts and UI text.
- Risk: incomplete workload ownership mappings could over-include or hide evidence.
- Mitigation: make mappings an explicit inclusion rule and show unmapped coverage instead of guessing.
- Risk: fan-out to four providers can still exceed interactive latency.
- Mitigation: parallel fan-out, bounded windows, per-source timeout budgets, and result caps.
- Risk: one source failure can hide usable evidence from other sources.
- Mitigation: best-effort aggregation with per-source coverage status and explicit partial-data messaging.
- Risk: time skew and timezone mismatch can produce misleading ordering.
- Mitigation: normalize to UTC at the source boundary and render local offsets only in the UI layer.

## Related documents

- Architecture map: docs/architecture/architecture.md
- Component design: docs/architecture/design.md
- Code navigation: docs/architecture/codebase-guide.md
- Pitfalls index: docs/pitfalls/index.md

## Quick links

- Jira: not linked
- Status: status.md
- Tests: test-plan.md
- Implementation modules: backend.md, frontend.md, decisions.md
