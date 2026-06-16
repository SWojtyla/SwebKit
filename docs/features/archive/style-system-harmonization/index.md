# Feature Overview - style-system-harmonization

---

title: "Feature Overview - style-system-harmonization"
owner: ""
status: "Planned"
jira: ""
created: "2026-06-13"
updated: "2026-06-13"

---

## Goal

Make SwebKit styling easier to reuse, harder to drift, and more visually consistent across every feature area without changing the app's core identity or rewriting unrelated UI behavior. Preserve the current AKS and API Client visual feel; the cleanup is about systematizing and harmonizing the implementation, not replacing the look users already like.

## Value

The app has grown into a serious operations surface, but styling is now expensive to extend. New UI work often creates local button, select, dropdown, chip, toolbar, and panel styles instead of reusing a small contract. A harmonized style system should let future features compose consistent controls quickly, reduce `app.css` churn, and make theme changes safer.

## Current Styling Review

**Global note: 6/10.**

The app is not in bad shape visually: it has a recognizable dark operations aesthetic, theme tokens, CSS isolation, Fluent UI usage, and several shared components. The issue is architectural consistency. Styling quality depends too much on which feature was touched last. Some areas look polished in isolation, but the underlying approach is uneven.

What is working:

- `wwwroot/app.css` already defines foundation tokens for color, spacing, typography, radius, shadows, z-index, overlays, native controls, and themes.
- Shared primitives exist: `RoutePageHeader`, `PageToolbar`, `Modal`, `ConfirmDialog`, `ContextMenu`, `Dropdown`, `EmptyState`, `ErrorCallout`, `SkeletonRows`, and dashboard tiles.
- Many component styles correctly live beside their Razor component, which matches the Blazor CSS isolation guidance in `docs/pitfalls/blazor-maui.md`.
- Some newer surfaces, especially Monitoring, use `app-native-control` and shared error/empty-state components more consistently.

What is holding the score down:

- `src/SwebKit.App/wwwroot/app.css` is 5,255 lines and mixes tokens, themes, base styles, shared helpers, and feature-specific selectors.
- There are 126 source CSS files and 22,099 lines of isolated component CSS under `src/SwebKit.App`.
- Components contain about 615 raw `<button>` occurrences and 54 raw `<select>` occurrences.
- The shared `Dropdown` component exists but no `<Dropdown>` usages were found in current Razor components.
- `PageToolbar` exists but only has two component usages, while several pages still hand-roll toolbar markup.
- Button class families are highly fragmented: `api-client-toolbar-btn`, `message-list-view__toolbar-button`, `dashboard-action-button`, `page-header-action-btn`, `incident-timeline-config__button`, `alert-rule-row__action-btn`, `obs-copy-btn`, `copy-btn`, and others all solve similar problems differently.
- Select styling is split across `app-native-control`, `app-native-select`, `form-input`, `filter-select`, `incident-scope-toolbar__select`, `auth-panel__type-select`, `dashboard-field`, and other local classes.
- Several component styles reference token names that are not defined in `app.css`, including `--color-input-bg`, `--color-surface-raised`, `--color-surface-hover`, `--font-mono`, and `--color-danger`. Some have fallbacks, some do not.
- A global `button:active { transform: scale(0.97); }` applies to every raw button, which is easy to forget and can create inconsistent interaction behavior.

## Scope

### In scope

- Audit the current app-wide styling model across Dashboard, Service Bus, AKS, Redis, Storage, Pipelines, Releases, Observability, Incident Timeline, Monitoring, API Client, Shared, Layout, and Notifications.
- Define a small design-system contract for buttons, icon buttons, select/dropdown controls, text inputs, tabs/segmented controls, chips/badges, toolbars, panels, dialogs, and empty/error/loading states.
- Split `app.css` into clearer ownership layers while keeping a stable entry point for Blazor/MAUI loading.
- Decide whether shared controls should be Razor components, global CSS classes, Fluent UI wrappers, or a mix.
- Migrate highest-drift areas first, especially API Client and AKS, then sweep older global helper classes.
- Add validation guidance so new features can follow the same styling path without re-learning the whole CSS tree.

### Out of scope

- No implementation in this planning step.
- No wholesale visual rebrand.
- No flattening or redesign of the current AKS and API Client visual direction.
- No replacement of every page layout in one pass.
- No removal of existing themes unless a later implementation review identifies broken or unused theme variants.
- No backend/domain changes.

## Feature Area Summary

| Area | Current state | Priority |
| --- | --- | --- |
| Shared | Good primitives exist, but they are thin and underused. `Dropdown` has no current usages. | High |
| API Client | Large local stylesheet and many bespoke toolbar/dialog/button/control styles. Also contains undefined token references. | Very high |
| AKS | Largest feature CSS footprint. Internally rich, but many AKS-specific global tokens live in `app.css`. | Very high |
| Dashboard | Visually polished but has one of the largest page-level stylesheets and many bespoke dashboard controls. | High |
| Service Bus | Mature feature styling with repeated local toolbar/action patterns. Uses shared context menu classes in places. | High |
| Incident Timeline | Uses `PageToolbar`, but scope selects and source toggles remain local variants. | Medium |
| Monitoring | Newer code follows `app-native-control` more often, but drawer/buttons are still feature-local. | Medium |
| Observability | Uses shared page header and toolbar patterns more than most areas, but large Observability-specific selectors still live globally. | Medium |
| Pipelines/Releases | Uses older shared helpers such as `form-input`, `filter-select`, and status badges. Needs convergence, not a rewrite. | Medium |
| Redis/Storage/Notifications | Mostly component-isolated styles with local button/action patterns. Good later migration targets. | Medium |

## Dependencies

- Architecture context: `docs/architecture/index.md`, `docs/architecture/architecture.md`, `docs/architecture/design.md`, `docs/architecture/codebase-guide.md`.
- Pitfalls: `docs/pitfalls/blazor-maui.md`, especially BL-11 on CSS isolation and the rule that cross-component shared styles belong in `app.css` or another global stylesheet.
- Repo memory: `memories/repo/editing-notes.md`, especially the note that `app.css` is patch-fragile.
- Active feature awareness: `docs/features/active/api-client/`, `docs/features/active/monitoring-alert-rules/`.
- Source touchpoints: `src/SwebKit.App/wwwroot/app.css`, `src/SwebKit.App/Components/Shared/`, `src/SwebKit.App/Components/**/*.razor`, `src/SwebKit.App/Components/**/*.razor.css`.

## Risks & Mitigations

- Risk: CSS split breaks MAUI Blazor loading or theme application. Mitigation: keep `app.css` as the stable entry point and verify imports/load order before moving rules.
- Risk: CSS isolation prevents parent/page styles from affecting child components. Mitigation: keep feature-specific styles beside components and move only true primitives to global layers.
- Risk: broad visual changes cause regressions across routes. Mitigation: migrate in waves with compatibility aliases and route-by-route visual checks.
- Risk: reusable components become too abstract and slow future work. Mitigation: start with the repeated controls already visible in the codebase: button, icon button, select/dropdown, toolbar, badge/chip, panel, dialog action footer.
- Risk: token cleanup changes theme contrast. Mitigation: validate dark, light, and alternate themes before removing aliases.

## Related Documents

- Status: `status.md`
- Test plan: `test-plan.md`
- Frontend plan and audit details: `frontend.md`
- Decisions: `decisions.md`

## Quick Links

- Jira: not linked
- Implementation modules: `frontend.md`, `decisions.md`