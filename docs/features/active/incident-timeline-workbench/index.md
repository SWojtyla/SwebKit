# Feature Overview - incident-timeline-workbench

---

title: "Feature Overview - incident-timeline-workbench"
owner: "GitHub Copilot"
status: "Planned"
jira: "not linked"
created: "2026-03-28"
updated: "2026-03-28"

---

## Goal

Deliver a single incident timeline workbench that correlates App Insights failures, AKS events and pod restarts, Service Bus DLQ activity, and release triggers in one time-ordered view to reduce time-to-triage.

## Value

Today incident investigation is fragmented across Observability, AKS, Service Bus, and Pipelines pages. Operators manually align timestamps and context, which increases triage time and increases the risk of false cause attribution. This feature creates one unified incident view with shared time range and source filters so responders can establish sequence-of-events quickly.

## Scope

- In scope:
- Wave 1 - Core timeline normalization contracts and aggregation orchestration in SwebKit.Core.
- Wave 2 - Source adapters for App Insights, AKS, Service Bus DLQ, and DevOps release triggers.
- Wave 3 - New Incident Timeline workbench page and components in SwebKit.App.
- Wave 4 - Performance, cancellation, partial-failure UX, and test hardening.
- Out of scope:
- Bi-directional remediation actions (restart, replay, approve) from timeline items.
- Persistent incident timelines or long-term timeline storage.
- Cross-tenant or cross-profile correlation in one query.
- New Azure resource provisioning or infrastructure changes.

## Dependencies

- Internal projects and paths:
- src/SwebKit.App for route, page shell, timeline components, and page-level state orchestration.
- src/SwebKit.Core for timeline models, abstractions, correlation service, and cancellation-aware orchestration.
- src/SwebKit.Observability for App Insights signal adapter using existing provider query surface.
- src/SwebKit.Kubernetes for AKS signal adapter based on events and restart metadata.
- src/SwebKit.Azure for Service Bus DLQ signal adapter.
- src/SwebKit.DevOps for release trigger signal adapter from existing pipeline run/release metadata.
- Architecture and functional docs expected to be updated during implementation:
- docs/architecture/functionalities/observability.md
- docs/architecture/functionalities/aks.md
- docs/architecture/functionalities/service-bus.md
- docs/architecture/functionalities/releases.md
- Pitfalls that apply:
- docs/pitfalls/blazor-maui.md
- docs/pitfalls/azure-sdk.md
- docs/pitfalls/dotnet-csharp.md

## Risks & mitigations

- Risk: Timeline fan-out to 4 providers can exceed interactive latency.
- Mitigation: Parallel fan-out with per-source timeout budgets, source-level caps, and progressive rendering.
- Risk: Cancellation races from rapid range/filter changes can leave stale data in UI.
- Mitigation: Cancellation-first refresh model, request version tokens, and strict stale-result dropping.
- Risk: One source failure can hide usable signals from other sources.
- Mitigation: Best-effort aggregation with per-source health and explicit partial-data banner.
- Risk: Time skew and timezone mismatch can produce incorrect event ordering.
- Mitigation: Normalize to UTC at source boundary and keep rendering offset-only in UI.
- Risk: High-frequency updates can saturate Blazor rendering.
- Mitigation: Buffered state updates and throttled StateHasChanged usage.

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
