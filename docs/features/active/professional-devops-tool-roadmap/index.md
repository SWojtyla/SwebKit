# Feature Overview - professional-devops-tool-roadmap

---

title: "Feature Overview - professional-devops-tool-roadmap"
owner: "GitHub Copilot"
status: "In Progress"
jira: "not linked"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Provide one dependency-aware delivery roadmap for evolving SwebKit into a more professional Azure operations tool, sequencing work from shell polish through domain-depth improvements without turning the roadmap itself into an implementation catch-all.

## Value

SwebKit already has strong capability seams across Service Bus, AKS, Observability, Redis, Storage, DevOps, and the MAUI Blazor Hybrid shell. What it does not yet have is a durable order of operations for improving the operator experience across those seams.

Without a master sequence, later features will naturally re-solve the same shell, navigation, readiness, and safety problems in parallel. This roadmap makes the order explicit:

- UX and shell consistency first.
- Shell-level navigation and workspace capabilities second.
- Configuration and operator readiness visibility third.
- Incident workflow expansion fourth.
- Domain-depth feature work only after the shared operator foundations are stable.

## Scope

- In scope:
- Define the delivery waves, dependency chain, and decision rules for future feature work.
- Establish the canonical order: UX/UI foundation, shell/navigation/workspaces, configuration health, incident workflows, then domain-depth features.
- Reference the active feature folders that implement each wave.
- Reference the detailed later-wave feature folders that now own incident follow-ons and domain-depth work.
- Out of scope:
- Application code changes.
- Replanning the already-active `incident-timeline-workbench` feature in place.
- Collapsing multiple future wave-4 or wave-5 initiatives into one mega-feature folder.

### Delivery waves

- Wave 1 - `shell-ux-foundation` (archived)
- Wave 2 - `operator-navigation-and-workspaces` (archived)
- Wave 3 - `environment-and-configuration-health`
- Wave 4 - `incident-investigation-workflows` on top of `incident-timeline-workbench`
- Wave 5A - `pipelines-deployment-assurance` and `service-bus-operator-workbench`
- Wave 5B - `aks-runtime-diagnostics-depth` and `observability-explainer-and-reliability`
- Wave 5C - `redis-ops-insights` and `storage-controlled-mutations`

## Dependencies

- Related features:
- `docs/features/archive/shell-ux-foundation/summary.md`
- `docs/features/archive/operator-navigation-and-workspaces/summary.md`
- `docs/features/active/environment-and-configuration-health/`
- `docs/features/active/incident-timeline-workbench/`
- `docs/features/active/incident-investigation-workflows/`
- `docs/features/active/service-bus-operator-workbench/`
- `docs/features/active/aks-runtime-diagnostics-depth/`
- `docs/features/active/observability-explainer-and-reliability/`
- `docs/features/active/redis-ops-insights/`
- `docs/features/active/pipelines-deployment-assurance/`
- `docs/features/active/storage-controlled-mutations/`
- Architecture constraints:
- `docs/architecture/architecture.md`
- `docs/architecture/design.md`
- `docs/architecture/codebase-guide.md`
- Functional docs that anchor later waves:
- `docs/architecture/functionalities/settings-and-configuration.md`
- `docs/architecture/functionalities/incident-timeline.md`
- Pitfall files that apply:
- `docs/pitfalls/agent-workflow.md`
- `docs/pitfalls/blazor-maui.md`
- `docs/pitfalls/dotnet-csharp.md`

## Risks & mitigations

- Risk: the roadmap becomes a catch-all specification instead of a sequencing document. Mitigation: keep implementation detail in the downstream feature folders and treat this feature as plan-of-plans only.
- Risk: later waves start before shell and readiness foundations are stable, causing duplicate UI patterns and inconsistent behavior. Mitigation: define explicit entry and exit criteria per wave in `roadmap.md`.
- Risk: `incident-timeline-workbench` gets silently absorbed into the roadmap scope. Mitigation: keep that feature separate and reference it only as a dependency for wave 4.
- Risk: domain-depth work expands into a single oversized backlog item. Mitigation: require a dedicated active feature folder for each wave-5 initiative before implementation begins.

## Related documents

- Architecture map: `docs/architecture/architecture.md`
- Component design: `docs/architecture/design.md`
- Code navigation: `docs/architecture/codebase-guide.md`
- Settings functionality: `docs/architecture/functionalities/settings-and-configuration.md`
- Incident functionality: `docs/architecture/functionalities/incident-timeline.md`
- Related active feature: `docs/features/active/incident-timeline-workbench/`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `roadmap.md`, `decisions.md`
