# Frontend Plan - style-system-harmonization

---

title: "Frontend Plan - style-system-harmonization"
owner: ""
status: "Planned"

---

## Goal

Give SwebKit a coherent UI styling system so new pages and feature components can use consistent buttons, dropdowns, form controls, toolbars, badges, chips, panels, dialogs, and page structure without copying local CSS from another feature.

## Impacted Areas

- Global stylesheet entry point: `src/SwebKit.App/wwwroot/app.css`
- Shared primitives: `src/SwebKit.App/Components/Shared/`
- Layout primitives: `src/SwebKit.App/Components/Layout/`
- High-drift feature areas: `src/SwebKit.App/Components/ApiClient/`, `src/SwebKit.App/Components/Aks/`, `src/SwebKit.App/Components/ServiceBus/`, `src/SwebKit.App/Components/Pages/`
- Broader migration targets: `src/SwebKit.App/Components/Monitoring/`, `IncidentTimeline/`, `Pipelines/`, `Releases/`, `Storage/`, `Redis/`, `Notifications/`, `Observability/`
- Tests: `tests/SwebKit.App.Tests/`, `tests/SwebKit.E2E.Tests/`

## Audit Findings

### Styling footprint

| Metric | Current value | Interpretation |
| --- | ---: | --- |
| `app.css` lines | 5,255 | Too large for safe global ownership; it mixes multiple concerns. |
| Source CSS files | 126 | CSS is spread widely, which is expected with isolation, but needs conventions. |
| Component-scoped CSS files | 125 | Component isolation is heavily used. Keep it for local layout. |
| Component-scoped CSS lines | 22,099 | Feature-level styles are large enough that shared primitives should reduce repeated control styling. |
| Raw `<button>` occurrences | 615 | Repeated command styling is a major drift source. |
| Raw `<select>` occurrences | 54 | Dropdown/select styling has multiple competing patterns. |
| `PageToolbar` usages | 2 | Shared toolbar primitive exists but is not broadly adopted. |
| `Dropdown` usages | 0 | Shared dropdown primitive exists but is not currently used. |

### Control drift

Common button families found in Razor markup include:

- `ctx-item`
- `api-client-toolbar-btn`
- `message-list-view__toolbar-button`
- `incident-timeline-config__button`
- `dashboard-action-button`
- `page-header-action-btn`
- `mdp-btn`
- `copy-btn`
- `obs-copy-btn`
- `alert-rule-row__action-btn`
- `batch-send-panel__btn`
- `batch-replay-panel__btn`
- `api-client-dialog__btn`

Common select/input styling families include:

- `app-native-control`
- `app-native-select`
- `form-input`
- `filter-select`
- `dashboard-field`
- `incident-scope-toolbar__select`
- `auth-panel__type-select`
- `obs-guided-input`
- `blob-mutation-dialog__select`
- `release-editor__input`

This is the core inconsistency: many classes encode the same semantic states (`primary`, `secondary`, `danger`, `ghost`, `active`, `disabled`, `compact`, `toolbar`) under feature-local names.

### Token drift

Several component styles use token names that are not defined in `app.css`:

- `--color-input-bg`
- `--color-surface-raised`
- `--color-surface-hover`
- `--font-mono`
- `--color-danger`

The current canonical equivalents appear to be closer to:

- `--control-surface`
- `--color-surface`, `--color-surface-2`, `--color-surface-3`, or `--color-panel-card`
- `--font-family-mono`
- `--color-error` or `--color-prod`, depending on destructive severity

Do not remove the older references blindly. Add aliases first or migrate feature CSS in controlled slices.

### Feature-area observations

- API Client is the best first migration target because it has a large stylesheet, many bespoke toolbar/dialog/action controls, and token drift.
- AKS has the largest component CSS total and many global `--aks-*` tokens. Its cleanup should separate true global primitives from AKS-specific layout/diagnostic affordances.
- Dashboard has strong visual polish, but the local styling is heavy and should be checked against the final panel/card/button contract.
- Monitoring is comparatively newer and closer to the desired direction because it uses `app-native-control` and shared error callouts, but it still has drawer-specific action buttons.
- Observability is closer to shared page structure through `RoutePageHeader`, `PageToolbar`, and Fluent buttons, but large Observability-specific CSS still lives in the global file.
- Incident Timeline uses `PageToolbar` but still owns local select/toggle styling that could become shared field/select and segmented-toggle primitives.

## Proposed Style System Contract

### CSS ownership layers

Keep `app.css` as the loaded entry point, but turn it into a structured entry file or an imported set of layers:

- `tokens.css`: spacing, typography, radius, shadow, z-index, semantic color aliases, control tokens.
- `themes.css`: `[data-theme]` blocks only.
- `base.css`: body, anchors, global focus, native control reset, OS select option compatibility.
- `primitives.css`: `app-button`, `app-icon-button`, `app-native-control`, `app-select`, `app-chip`, `app-badge`, `app-toolbar`, `app-panel`, `app-dialog-actions`.
- `utilities.css`: small text/layout helpers that are intentionally global.
- `legacy.css`: compatibility aliases during migration, with a removal checklist.

If import behavior creates Blazor/MAUI issues, keep one physical `app.css` but reorganize it under the same named sections.

### Shared Razor primitives

Add or harden shared components only where markup semantics matter:

- `AppButton`: variants `Primary`, `Secondary`, `Ghost`, `Danger`, `Subtle`; sizes `Small`, `Medium`; optional `IconStart`, `IconEnd`; supports disabled/loading.
- `AppIconButton`: icon-only command with required accessible label and tooltip/title.
- `AppSelect` or `FormField`: wraps label, validation, `app-native-control`, and consistent sizing for native selects.
- `AppDropdown`: replace the current bare `Dropdown` with alignment, width, menu item, keyboard, and focus behavior.
- `SegmentedControl`: consistent tab/mode toggles for body mode, density, source toggles, and query modes.
- `StatusBadge` / `AppChip`: consistent status, source, method, severity, and environment pills.
- `PageToolbar`: keep, but extend with density, wrapping, and action-group conventions before broad adoption.

Do not create a giant all-purpose component. Each primitive should solve one repeated pattern already visible in the app.

## Implementation Waves

### Wave 0 - Contract and safety net

- Document canonical control variants and token names.
- Add missing token aliases for known legacy references.
- Decide whether `--color-danger` maps to `--color-error` or remains a separate semantic token.
- Add a small style inventory script or test helper that reports new raw control class families.
- Update architecture/codebase guidance with style ownership rules.

### Wave 1 - Foundation primitives

- Extract or reorganize `app.css` into named layers while preserving load order.
- Implement `AppButton`, `AppIconButton`, `AppSelect` or `FormField`, `AppDropdown`, `SegmentedControl`, and `StatusBadge` only if agreed.
- Add component tests for variants, disabled/loading states, and accessibility attributes.

### Wave 2 - High-drift migration

- Migrate API Client toolbar, dialog, warning/conflict actions, tabs, and select/input classes to shared primitives or canonical classes while preserving its current visual direction.
- Migrate one AKS toolbar/grid action slice, not the whole AKS area at once, and keep the current AKS diagnostics feel intact.
- Preserve legacy classes as aliases where needed during transition.

### Wave 3 - Cross-feature sweep

- Move Service Bus, Incident Timeline, Monitoring, Dashboard, Pipelines/Releases, Storage, Redis, Notifications, and Observability to the same control primitives.
- Remove duplicated `.form-input`, `.filter-select`, local copy-button, and toolbar-button definitions once no longer used.
- Remove compatibility aliases only after tests and route smoke checks pass.

## Accessibility and UX Requirements

- Icon-only buttons must have an accessible label and visible hover/focus affordance.
- Native selects must preserve readable OS popup colors across dark and light themes.
- Focus outlines must be visible in all supported themes.
- Disabled and loading states must be visually distinct and keyboard-safe.
- Destructive actions must use the existing production-safety cues where relevant.
- Toolbar controls must not wrap or overlap in normal desktop widths.

## Validation

- Component tests: planned for shared primitives.
- Manual UX checks: planned across dark, light, and alternate themes.
- E2E smoke: planned for main routes after migration waves.

## Notes

- Follow `docs/pitfalls/blazor-maui.md`: CSS for child component internals belongs beside the child component, not in the parent page stylesheet.
- Keep global styles for true primitives only. Feature-specific layout should remain isolated.
- Avoid a single big migration PR; this work should be sliced by primitive and feature area.
- Treat AKS and API Client as visual reference surfaces. They need cleaner reusable styling contracts, not a visual reset.