# Feature Overview - operator-navigation-and-workspaces

---

title: "Feature Overview - operator-navigation-and-workspaces"
owner: "GitHub Copilot"
status: "Planned"
jira: "not linked"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Give SwebKit a coherent shell-level navigation and workspace model so operators can find resources faster, return to recent context, pin important assets, and save named investigation workspaces that restore meaningful cross-page state.

## Value

The shell already has the right seeds for this feature: a command palette, recent command history, persistent open tabs, pinned entities, and selection context services. What it does not yet have is one unified model that ties those seams together into professional operator workflows.

Today the command palette mixes commands with an ad hoc `go ` mode, favorites only appear as dashboard pins, and page context is largely page-local. Operators doing repeated investigations or switching between resources have to rebuild context manually.

## Scope

- In scope:
- Command palette precision improvements and unified resource search.
- Shell-level recent and favorite resources.
- A named workspace model for restoring investigation context across major operator pages.
- A consistent shell contract for pages that want to contribute searchable resources or workspace snapshots.
- Out of scope:
- Shell chrome polish already owned by `shell-ux-foundation`.
- First-run configuration and readiness health.
- New domain-specific operations inside Service Bus, AKS, Redis, Storage, Pipelines, Observability, or Incident Timeline.

### Delivery waves

- Wave 1 - command palette precision and unified resource search
- Wave 2 - recent and favorite resources at shell level
- Wave 3 - saved investigation workspaces and restore semantics

## Dependencies

- Hard dependency:
- `docs/features/active/shell-ux-foundation/`
- Sequencing parent:
- `docs/features/active/professional-devops-tool-roadmap/`
- Future consumer dependency to keep separate:
- `docs/features/active/incident-timeline-workbench/`
- Architecture and code navigation:
- `docs/architecture/architecture.md`
- `docs/architecture/design.md`
- `docs/architecture/codebase-guide.md`
- Pitfall files that apply:
- `docs/pitfalls/blazor-maui.md`
- `docs/pitfalls/dotnet-csharp.md`

## Risks & mitigations

- Risk: workspace persistence captures brittle component internals and becomes impossible to version safely. Mitigation: persist semantic route/resource/filter models, not raw component object state.
- Risk: palette search becomes slower as resource types increase. Mitigation: use provider registration and explicit ranking rather than page-specific one-off queries.
- Risk: favorites, recents, tabs, and workspaces drift into multiple overlapping persistence stores. Mitigation: define one storage split and document it in `decisions.md`.
- Risk: workspace restore causes stale updates on disposed components. Mitigation: use contributor contracts that are cancellation-aware and route-first.

## Related documents

- Architecture map: `docs/architecture/architecture.md`
- Component design: `docs/architecture/design.md`
- Code navigation: `docs/architecture/codebase-guide.md`
- Incident workflow reference: `docs/features/active/incident-timeline-workbench/`
- Sequencing roadmap: `docs/features/active/professional-devops-tool-roadmap/`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `frontend.md`, `backend.md`, `decisions.md`
