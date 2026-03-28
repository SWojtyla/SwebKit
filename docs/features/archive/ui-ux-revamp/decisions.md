# Decisions — ui-ux-revamp

---

title: "Decisions — ui-ux-revamp"
owner: ""
created: "2026-03-22"
updated: "2026-03-22"

---

## Decision Log

---

### D-1 — Support All Four Light Palettes Simultaneously

**Date:** 2026-03-22  
**Status:** Decided

**Context:**  
The plan originally proposed four named light color palettes (Azure Bloom, Coral Studio, Forest Dev, Violet Cloud) and asked the user to pick one for implementation.

**Decision:**  
All four palettes are implemented and selectable. The theme picker in Appearance settings exposes five options: Dark, Light — Azure Bloom, Light — Coral Studio, Light — Forest Dev, Light — Violet Cloud.

Each palette maps to a distinct `data-theme` attribute value:

| Theme name   | `data-theme` value   |
| ------------ | -------------------- |
| Dark         | `dark`               |
| Azure Bloom  | `light-azure-bloom`  |
| Coral Studio | `light-coral-studio` |
| Forest Dev   | `light-forest-dev`   |
| Violet Cloud | `light-violet-cloud` |

**Rationale:**  
The palettes have different personalities (professional, warm, earthy, playful) that appeal to different preferences and environments. Since they are just CSS variable blocks with no runtime overhead, shipping all four costs very little. Restricting to one would be an arbitrary limitation given they are already fully designed.

**Consequences:**

- Phase 3 needs to ship all four `[data-theme="light-*"]` blocks in `app.css` instead of one.
- `FluentDesignTheme` mode switching uses `_currentTheme != "dark"` (any non-dark value → Light mode).
- The `localStorage` key `"swebkit-ui-theme"` stores one of the five string values above.
- The Appearance settings dropdown lists all five options.
- No palette selection pre-requisite blocks Phase 3.

**Alternatives considered:**

- Pick one — rejected: arbitrary given full palette specs already exist and CSS cost is negligible.
- Offer as separate "color accent" option on top of a single light theme — rejected: more complex to implement, palettes differ in background and border colors, not just accent.

---

### D-2 — Theme Stored in `localStorage`, Not in `profiles.json`

**Date:** 2026-03-22  
**Status:** Decided

**Context:**  
Theme preference could be stored in the existing `AppConfig`/`profiles.json` persistence layer or in browser `localStorage`.

**Decision:**  
Use `localStorage` via `IJSRuntime`. Do not extend `AppConfig` or `UiStateRepository`.

**Rationale:**  
Theme choice is a local visual preference specific to the device, not a profile that travels with configured connections. Keeping it out of the profile keeps `AppConfig` lean and avoids domain model changes.

**Consequences:**

- `MainLayout.razor` reads/writes `localStorage` key `"swebkit-ui-theme"` in `OnAfterRenderAsync(firstRender)` (required for MAUI Blazor Hybrid — see BL-6).
- Resetting profiles does not reset the theme.

---

### D-3 — Use `data-theme` Attribute on `.app-shell`, Not a CSS Class

**Date:** 2026-03-22  
**Status:** Decided

**Context:**  
Theming could be driven by a CSS class (e.g., `.theme-dark`) or a `data-*` attribute on the root shell element.

**Decision:**  
Use `data-theme="<value>"` on the `.app-shell` div.

**Rationale:**  
Attribute selectors (`[data-theme="dark"]`) are more explicit than class selectors for configuration state. They do not conflict with Fluent UI class names and are easy to set via a single `setAttribute` JS call.

---

### D-4 — Per-Feature Nav Icon Colors via `data-area` CSS Attribute Selectors

**Date:** 2026-03-22  
**Status:** Decided

**Context:**  
The current nav renders all icons with the same `color: var(--color-text-muted)` — no visual distinction between features.

**Decision:**  
Add `data-area="@Area"` to the `.nav-item` div in `NavItem.razor`. CSS rules in `app.css` target `[data-area="X"] fluent-icon` to apply per-feature accent colors defined as CSS variables (`--color-nav-dashboard`, etc.).

**Rationale:**  
One-line Razor change, pure CSS implementation, works in both collapsed and expanded nav states, and is trivially themeable by varying the variable values per `[data-theme]` block.
