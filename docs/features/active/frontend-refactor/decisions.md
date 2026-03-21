# Frontend Refactor — Decisions

## Decision 1 — CSS isolation file splitting for `AksPage`

**Date:** 2026-03-21

**Question:** Blazor CSS isolation bundles one `.razor.css` file per component. How do we split `AksPage.razor.css` (1,183 lines) into logical sub-files?

**Options:**
1. Use `@import` inside `AksPage.razor.css` to pull in sub-files (processed at build time by the bundler)
2. Move sub-concerns to child components (e.g. `HpaPanel.razor`) so each gets its own `.razor.css`
3. Keep one file but add structured comment headers as sections

**Decision:** Option 2 is preferred because it aligns with Phase 3 (component splitting). As child components are extracted, their CSS moves with them naturally. For CSS that belongs to the page itself, Option 1 (`@import`) is used as an interim measure.

Option 3 (comment headers) is rejected — it only helps readability, not maintainability.

---

## Decision 2 — `AutoRefreshController` as component vs. service

**Date:** 2026-03-21

**Question:** Should `AutoRefreshController` be a Blazor component (`<AutoRefreshController />`) or a plain C# class injected via DI?

**Options:**
1. Plain C# class instantiated per-component (not DI-registered)
2. Blazor component with `ChildContent` render fragment
3. Scoped DI service

**Decision:** Option 1. Auto-refresh is a UI concern tied to a specific component's lifecycle. It should be instantiated in `OnInitialized` and disposed in `DisposeAsync`. No global state needed — DI registration is unnecessary complexity.

---

## Decision 3 — Primitive component library vs. utility CSS classes

**Date:** 2026-03-21

**Question:** For common UI patterns (buttons, inputs), should we create Razor components or CSS utility classes?

**Decision:** Mix:
- **CSS utility classes** for pure styling with no behavior (`.form-input`, `.surface-card`, `.text-muted`)
- **Razor components** only when behavior is involved (`<Modal />` — handles click-outside, `<Dropdown />` — handles backdrop)

Native HTML elements with class names are preferred over wrapper components for simple cases. This avoids unnecessary component overhead and keeps the component tree shallow.
