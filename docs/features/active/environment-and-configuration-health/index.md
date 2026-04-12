# Feature Overview - environment-and-configuration-health

---

title: "Feature Overview - environment-and-configuration-health"
owner: "GitHub Copilot"
status: "Planned"
jira: "not linked"
created: "2026-04-12"
updated: "2026-04-12"

---

## Goal

Make SwebKit explicit about what is configured, what credentials are available, which Azure-focused workflows are actually ready, and how environments differ, so operators can trust the app before they begin diagnosis or operational work.

## Value

SwebKit already exposes bits of health and configuration state across the dashboard, settings, connection indicators, and page-specific errors. The missing piece is one operator-facing readiness story.

Right now an operator often discovers missing configuration or credential issues only after entering a feature page. That is too late for a professional operations tool. This feature should turn environment readiness into an explicit first-class experience.

## Scope

- In scope:
- First-run setup checklist and next-step guidance.
- Credential/configuration health visibility across the major capability areas.
- Connection-health overview that explains configured vs ready vs failing states.
- Environment comparison for profile-based environments.
- Explicit operator readiness for Azure-focused workflows such as Service Bus, AKS, Observability, Storage, and DevOps.
- Out of scope:
- New shell navigation/workspace capabilities.
- New domain-specific operations inside feature pages.
- Any mutating or expensive "health check" that changes external resources.

### Delivery waves

- Wave 1 - first-run checklist and configuration inventory
- Wave 2 - credential/config health and connection-health overview
- Wave 3 - environment comparison and operator readiness summary

## Dependencies

- Hard dependency:
- `docs/features/active/shell-ux-foundation/`
- Sequencing dependency:
- `docs/features/active/operator-navigation-and-workspaces/`
- Sequencing parent:
- `docs/features/active/professional-devops-tool-roadmap/`
- Architecture and functionality references:
- `docs/architecture/architecture.md`
- `docs/architecture/design.md`
- `docs/architecture/codebase-guide.md`
- `docs/architecture/functionalities/settings-and-configuration.md`
- Pitfall files that apply:
- `docs/pitfalls/blazor-maui.md`
- `docs/pitfalls/dotnet-csharp.md`
- `docs/pitfalls/azure-sdk.md`

## Risks & mitigations

- Risk: health checks become expensive or accidentally mutative. Mitigation: require read-only, time-budgeted probes and document that rule in `decisions.md`.
- Risk: the app leaks credential details while trying to explain readiness. Mitigation: report reference presence and health, never secret material.
- Risk: environment comparison creates noise by diffing ephemeral or secret-backed fields. Mitigation: compare normalized config and reference metadata only.
- Risk: readiness copy becomes another passive dashboard that operators ignore. Mitigation: make checklist and health states actionable, with direct Settings handoff.

## Related documents

- Architecture map: `docs/architecture/architecture.md`
- Component design: `docs/architecture/design.md`
- Code navigation: `docs/architecture/codebase-guide.md`
- Settings functionality: `docs/architecture/functionalities/settings-and-configuration.md`
- Sequencing roadmap: `docs/features/active/professional-devops-tool-roadmap/`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `frontend.md`, `backend.md`, `decisions.md`
