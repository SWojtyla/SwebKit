# Feature Overview — ui-ux-revamp

---

title: "Feature Overview — ui-ux-revamp"
owner: ""
status: "Proposed"
created: "2026-03-22"
updated: "2026-03-22"

---

## Goal

Modernize the SwebKit visual design so the app feels alive, enjoyable, and polished while remaining a practical developer tool. Introduce a second "colorful light" theme alongside the existing dark theme, add per-feature sidebar icon colors, redesign the dashboard to use wide-screen space better, clean up inline styles across all pages, and apply a consistent global style system (shadows, radii, micro-animations, typography scale).

## Value

- Removes the rigid, monochrome feel that makes extended daily use fatiguing.
- Light theme gives users a choice appropriate for bright environments or personal preference.
- Per-feature icon colors add spatial orientation — users know which area they're in at a glance.
- A richer dashboard surfaces more actionable at-a-glance information.
- Eliminating inline styles makes future visual changes faster and less error-prone.
- A consistent design token system (`--shadow-*`, `--radius-*`) reduces decision fatigue across contributors.

## Scope

**In scope:**

- CSS variable restructure to support multiple themes via a `data-theme` attribute
- Light theme implementation: four palette proposals, one selected for implementation
- `FluentDesignTheme` wired to match selected light/dark state
- Theme selector in `SettingsPage.razor`
- Theme preference persisted in `localStorage` via JSInterop
- Per-feature sidebar icon accent colors in `NavItem.razor` and `app.css`
- Dashboard layout redesign: full-width, hero stats row, richer health tiles, 2-column lower layout
- `SettingsPage` two-column layout with nav sidebar + content well
- `ReleasesPage` inline-to-class migration, pill tabs, improved release selector
- `ServiceBusPage` inline-to-class migration, improved entity pane visual hierarchy
- `AksPage` inline-to-class migration, color-coded pod status badges
- `RedisPage` and `StoragePage` layout and spacing cleanup
- `TopBar` hardcoded color removal, environment indicator polish
- `StatusBar` informational improvement
- Global shadow system (`--shadow-sm/md/lg`)
- Global border-radius tokens (`--radius-sm/md/lg`)
- Typography scale bump (base →14px)
- Micro-animation pass: nav hover, button press, card expand transitions

**Out of scope:**

- Swapping or removing the Fluent UI Blazor component library
- Adding npm build steps or bundlers
- Server-side theme storage
- New feature functionality (no domain or backend changes)
- MAUI native chrome (title bar, window decoration)
- Full accessibility audit (tracked separately)

## Dependencies

- `Microsoft.FluentUI.AspNetCore.Components` — `FluentDesignTheme` component must coordinate with CSS variable overrides
- `app.css` — single source of truth for all design tokens
- `localStorage` (via IJSRuntime) — theme preference storage; requires JSInterop (runs `OnAfterRenderAsync`)
- `SettingsPage.razor` — needs a new "Appearance" section for theme selection

## Risks & Mitigations

- **Risk:** `FluentDesignTheme` applies its own CSS custom properties that may conflict with custom `--color-*` variables. — **Mitigation:** Use `data-theme` overrides layered on top; test both Fluent dark and Fluent light modes explicitly before shipping Phase 3.
- **Risk:** Theme switch in a Blazor Hybrid app requires JS interop to set `data-theme` on `document.body`; must wait until after first render (BL-6). — **Mitigation:** Theme init in `OnAfterRenderAsync(firstRender)` in `MainLayout.razor`.
- **Risk:** Removing `TopBar` hardcoded `#1E1E2E` background may expose Fluent Design System styles bleeding into the top bar area. — **Mitigation:** Test early in Phase 2 before other pages are touched.
- **Risk:** Inline-style cleanup on `ServiceBusPage` and `ReleasesPage` may collide with Fluent component rendered styles. — **Mitigation:** Use scoped `.razor.css` files where Fluent components are parent; use global `app.css` for structural grid/layout rules only.
- **Risk:** Dashboard refactor touches `DashboardPage.razor.css` and may affect `HealthTile` component sizing. — **Mitigation:** HealthTile is a standalone component; changes to the tile container grid do not require tile template changes unless opted in.

## Related Documents

- Architecture: [docs/architecture/architecture.md](../../architecture/architecture.md)
- Design: [docs/architecture/design.md](../../architecture/design.md)
- Pitfalls (Blazor/MAUI): [docs/pitfalls/blazor-maui.md](../../../pitfalls/blazor-maui.md)
- Pitfalls (index): [docs/pitfalls/index.md](../../../pitfalls/index.md)

## Quick Links

- Status: [status.md](status.md)
- Frontend plan: [frontend.md](frontend.md)
- Tests: n/a (visual / manual validation only for this feature)
