# Feature Overview - shell-ux-foundation

---

title: "Feature Overview - shell-ux-foundation"
owner: "GitHub Copilot"
status: "Review"
jira: "not linked"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Establish one consistent shell UX for SwebKit so that every routed page inherits reliable navigation state, page context, empty/loading/error treatment, refresh/status language, notification behavior, theme polish, and production-safety cues.

## Value

The current shell already has the right structural seams: `MainLayout`, `LeftNav`, `TopBar`, `StatusBar`, shared UI primitives, and routed pages. The problem is consistency. Header hierarchy, toolbar structure, empty states, refresh affordances, and status language vary by page, which makes the app feel less trustworthy than the underlying capabilities deserve.

This feature is the foundation for every later operator experience improvement. It should make the shell feel deliberate and stable before more search, workspace, health, or incident flows are layered on top.

## Scope

- In scope:
- Route-aware navigation state instead of purely imperative area tracking.
- Navigation grouping and clearer shell hierarchy.
- Better top-bar context for current page and active environment.
- Consistent page headers and `h1` usage across routed pages.
- Stronger empty states with actionable CTAs.
- Trustworthy refresh and status signals in the shell.
- Notification-center polish and theme consistency.
- Shared production-safety treatment and consistent loading/error/empty-state patterns.
- Out of scope:
- Command palette resource search and saved workspaces.
- First-run readiness checks and environment comparison.
- New domain-specific workflows for Service Bus, AKS, Observability, or incident investigation.

### Delivery waves

- Wave 1 - shell context and navigation structure
- Wave 2 - page-header, state-pattern, and status-signal standardization
- Wave 3 - notification, theme, and production-safety polish

## Dependencies

- Architecture and code navigation:
- `docs/architecture/architecture.md`
- `docs/architecture/design.md`
- `docs/architecture/codebase-guide.md`
- Functional references:
- `docs/architecture/functionalities/settings-and-configuration.md`
- `docs/architecture/functionalities/incident-timeline.md`
- Sequencing parent:
- `docs/features/active/professional-devops-tool-roadmap/`
- Pitfall files that apply:
- `docs/pitfalls/blazor-maui.md`
- `docs/pitfalls/dotnet-csharp.md`

## Risks & mitigations

- Risk: route state and shell state diverge, leaving the wrong nav item active. Mitigation: derive shell context from the actual route rather than from click handlers alone.
- Risk: CSS isolation changes fix one page and break another. Mitigation: keep child-component styling in component-local CSS and shared tokens in global shell styles.
- Risk: standardized empty/loading/error patterns become too generic and remove useful page-specific nuance. Mitigation: unify structure and action placement, not domain-specific copy.
- Risk: production-safety cues become visually noisy. Mitigation: concentrate the persistent cues at shell level and keep heavy confirmation only on destructive actions.

## Related documents

- Architecture map: `docs/architecture/architecture.md`
- Component design: `docs/architecture/design.md`
- Code navigation: `docs/architecture/codebase-guide.md`
- Settings functionality: `docs/architecture/functionalities/settings-and-configuration.md`
- Related sequencing doc: `docs/features/active/professional-devops-tool-roadmap/`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `frontend.md`, `decisions.md`
