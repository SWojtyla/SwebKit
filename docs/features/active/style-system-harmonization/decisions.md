# Decisions - style-system-harmonization

---

title: "Decisions - style-system-harmonization"
owner: ""
status: "Planned"

---

## Decision 001 - Keep `app.css` as the stable entry point

**Status:** Proposed

**Date:** 2026-06-13

### Context

`src/SwebKit.App/wwwroot/app.css` is currently the app's global stylesheet and is loaded by the Blazor/MAUI host. It is large, but it is also the known working entry point for global tokens and theme rules.

### Decision

Keep `app.css` as the stable entry point during migration. Either split it into imported layered files or reorganize it under clear sections, but do not change the runtime entry point until load-order behavior is verified.

### Consequences

- Reduces risk while making ownership clearer.
- Allows incremental cleanup without breaking the app shell.
- Requires explicit validation of CSS imports if physical files are introduced.

### Alternatives considered

- Replace `app.css` with multiple direct stylesheet links - rejected for the first wave because it creates more host/load-order risk.
- Leave `app.css` as-is and only add components - rejected because the global file is already too large and patch-fragile.

---

## Decision 002 - Use global styles for primitives and isolated CSS for feature layout

**Status:** Proposed

**Date:** 2026-06-13

### Context

Blazor CSS isolation does not style child component internals from a parent stylesheet. Shared controls need global or component-owned styling, while feature-specific layout should stay close to the feature.

### Decision

Global style layers should own only primitives, tokens, base resets, utilities, and transitional aliases. Feature layout, grids, panels, and domain-specific arrangements should remain in `.razor.css` beside the relevant component.

### Consequences

- Prevents the global stylesheet from becoming a feature bucket again.
- Matches the existing Blazor pitfall guidance.
- Requires a clear rule for when a class graduates from feature-local to shared primitive.

### Alternatives considered

- Move most CSS into global files - rejected because it fights CSS isolation and increases selector coupling.
- Keep all CSS isolated - rejected because shared controls need consistent cross-component styling.

---

## Decision 003 - Introduce shared control primitives before broad migration

**Status:** Proposed

**Date:** 2026-06-13

### Context

The app has hundreds of raw button instances and many local select/dropdown styles. Migrating page by page without primitives would mostly rename drift.

### Decision

Define reusable control primitives first, then migrate high-drift surfaces to prove the model. Start with button, icon button, select/form field, dropdown/menu, segmented control, status badge/chip, toolbar, and dialog actions.

### Consequences

- Future features get a clearer path for common UI.
- Migration can be measured by reducing local button/select class families.
- Shared components must stay small and avoid hiding feature-specific behavior.

### Alternatives considered

- Use only Fluent UI components directly everywhere - keep as an option for some controls, but rejected as the only rule because the app already needs custom operational styling and MAUI/WebView-specific native control behavior.
- Use only CSS classes with raw HTML - rejected as the only rule because accessibility and state handling for icon buttons/dropdowns benefit from components.

---

## Decision 004 - No visual rebrand in the first cleanup

**Status:** Proposed

**Date:** 2026-06-13

### Context

The request is about consistency, maintainability, and styling best practices. A broad visual redesign would make review harder and blur functional regressions with taste changes.

### Decision

The first implementation should preserve the existing SwebKit operations-tool identity, with AKS and API Client treated as visual reference surfaces. Improve harmony through tokens, spacing, state behavior, and shared controls rather than a new visual direction.

### Consequences

- Lower review risk.
- Makes before/after validation easier.
- Protects the current AKS and API Client look while making their styling easier to reuse and maintain.
- A future design refresh can happen after the style system is stable.

### Alternatives considered

- Redesign all pages at once - rejected because it would be too broad and risky.
- Only shrink `app.css` without visual consistency work - rejected because the user's main pain includes inconsistent dropdowns and buttons.