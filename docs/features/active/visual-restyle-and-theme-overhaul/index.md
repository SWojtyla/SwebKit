# Feature Overview - visual-restyle-and-theme-overhaul

---

title: "Feature Overview - visual-restyle-and-theme-overhaul"
owner: "GitHub Copilot"
status: "In Progress"
jira: "not linked"
created: "2026-04-15"
updated: "2026-04-16"

---

## Goal

Restyle SwebKit so it feels more polished, pleasant, and trustworthy to use while keeping the existing global layout and route structure intact, with special focus on a stronger theme system and clearer, better-looking tables.

## Value

SwebKit already has strong functional coverage, but the visual language is uneven. The app has a real theme host and token system in `src/SwebKit.App/wwwroot/app.css`, yet many pages still rely on inline styles, page-specific table markup, and one-off surface treatments.

That inconsistency shows up most clearly in table-heavy areas: column headers, row density, sort affordances, empty states, and selected or destructive states do not feel like they belong to one product. A deliberate app-wide restyle should improve day-to-day readability and operator confidence without forcing users to relearn navigation.

## Scope

- In scope:
- Define and implement a refined visual direction for the app shell, shared surfaces, typography, spacing, and states.
- Overhaul the theme system so dark and light themes share a richer semantic token model and feel intentionally designed rather than color-swapped.
- Implement a low-cost in-app pilot that compares two candidate dark design languages before the full rollout is locked.
- Rework the appearance/settings experience so theme selection feels more curated and informative.
- Create a shared table system covering headers, row states, selection, sorting cues, density, truncation/wrapping, and sticky behavior where appropriate.
- Migrate high-visibility areas to the new visual foundation: Service Bus, AKS, Storage, Pipelines/Releases, Redis, Observability, Dashboard, and Settings.
- Reduce inline style usage where it blocks consistency, maintainability, or theme fidelity.
- Out of scope:
- Changing the global shell layout, navigation model, or route structure.
- Rewriting business workflows or backend contracts unless a UI primitive requires a small supporting change.
- Replacing Fluent UI with a different component library.
- Turning this into a one-shot rewrite of every page before shared primitives exist.

### Planned phases

1. Phase 1 - In-app art direction pilot (`Command Deck` vs `Studio Ledger`)
2. Phase 2 - Choose direction and complete token audit
3. Phase 3 - Theme overhaul
4. Phase 4 - Shared shell and surface primitives
5. Phase 5 - Shared table system
6. Phase 6 - Feature-area adoption and cleanup
7. Phase 7 - Validation and docs alignment

## Dependencies

- Related features:
- `docs/features/active/professional-devops-tool-roadmap/`
- Existing framework and theme host:
- `src/SwebKit.App/wwwroot/app.css`
- `src/SwebKit.App/Components/Layout/MainLayout.razor`
- `src/SwebKit.App/Components/Shared/AppearanceSettings.razor`
- `src/SwebKit.Core/Configuration/UserSettingsRepository.cs`
- Pitfall files that apply:
- `docs/pitfalls/agent-workflow.md`
- `docs/pitfalls/blazor-maui.md`
- Architecture constraints:
- `docs/architecture/architecture.md`
- `docs/architecture/design.md`
- `docs/architecture/codebase-guide.md`

## Risks & mitigations

- Risk: the restyle expands into an uncontrolled multi-page rewrite. Mitigation: keep layout and routing fixed, build shared primitives first, and migrate pages in explicit waves.
- Risk: visual direction stays abstract and subjective for too long. Mitigation: compare two live pilot directions on shell chrome, Dashboard, AKS, and one real table workflow before locking the direction.
- Risk: theme changes reduce contrast or weaken production safety cues. Mitigation: preserve semantic danger, warning, success, and production tokens and add explicit contrast checks.
- Risk: table cleanup stalls because current implementations are fragmented across inline markup and page-specific CSS. Mitigation: define one shared table contract before large-scale page adoption.
- Risk: Fluent styling and custom CSS drift further apart. Mitigation: treat global tokens plus `MainLayout` theme state as the single source of truth.

## Related documents

- Architecture map: `docs/architecture/architecture.md`
- Component design: `docs/architecture/design.md`
- Code navigation: `docs/architecture/codebase-guide.md`
- Settings functionality: `docs/architecture/functionalities/settings-and-configuration.md`
- Pitfalls: `docs/pitfalls/agent-workflow.md`, `docs/pitfalls/blazor-maui.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `frontend.md`, `layout.md`, `decisions.md`
