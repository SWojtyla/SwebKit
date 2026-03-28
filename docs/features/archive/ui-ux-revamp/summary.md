# Archive Summary — ui-ux-revamp

---

title: "Archive Summary — ui-ux-revamp"
owner: ""
completed_date: "2026-03-23"
pr: ""
commit: ""

---

## Goal

Modernize SwebKit's visual design: introduce a multi-theme system (dark + four light palettes), add per-feature sidebar icon colors, redesign the dashboard for wide-screen use, clean up inline styles across pages, and establish a consistent global design token system (shadows, radii, micro-animations, typography scale).

## Delivered

- **Theme infrastructure:** CSS variable restructure with `data-theme` attribute on `.app-shell`; `:root` holds layout/spacing/typography/radius/shadow constants; theme-specific color blocks for dark and four light themes
- **Five selectable themes:** Dark, Azure Bloom, Coral Studio, Forest Dev, Violet Cloud — all wired via `FluentDesignTheme` mode switching and persisted to `localStorage`
- **Theme selector:** `AppearanceSettings.razor` component in Settings page with `FluentSelect` dropdown; `MainLayout.razor` exposes `SetThemeAsync()` and `CurrentTheme` as a cascading value
- **Dark theme polish:** `TopBar.razor` hardcoded colors removed and replaced with CSS variables; `--color-surface-3` added for top bar / status bar differentiation
- **Per-feature nav icon colors:** `NavItem.razor` emits `data-area` attribute; CSS rules in `app.css` for all 7 areas with active/inactive states and `color-mix()` backgrounds; hover micro-animation
- **Dashboard redesign:** Hero stats row (4 stat chips), health tile grid with feature-colored borders and box-shadow, 2-column lower layout (activity feed 2/3 + pinned 1/3), full-width layout
- **Settings page:** Two-column layout with sidebar nav + content well; Appearance section as first item
- **Inline-style cleanup:** ReleasesPage (pill tabs, release selector), StoragePage (shell/panes), AksPage/PodGrid (pod status badges) — all migrated from inline styles to CSS classes
- **Global style polish:** Shadow variables (`--shadow-sm/md/lg`) and border-radius tokens (`--radius-sm/md/lg`) applied across components; micro-animations (button press scale, command palette fadeIn, nav hover transition); typography base bumped to 14px
- **New design tokens:** `--shadow-sm/md/lg`, `--radius-sm/md/lg`, `--color-surface-3`, `--color-text-faint`, per-feature nav accent variables

## Key decisions

- **D-1 All four light palettes shipped** — CSS variable blocks have zero runtime overhead; restricting to one would be arbitrary. Five themes selectable from settings.
- **D-2 Theme in `localStorage`, not `profiles.json`** — Theme is a device-local visual preference, not a profile setting. Keeps `AppConfig` lean.
- **D-3 `data-theme` attribute over CSS class** — Attribute selectors are more explicit for configuration state, don't conflict with Fluent UI class names, and are trivially set via `setAttribute`.
- **D-4 Per-feature nav colors via `data-area` CSS selectors** — One-line Razor change, pure CSS implementation, works in both collapsed and expanded nav, and is themeable per `[data-theme]` block.

## Validation performed

- Build passes with zero warnings and zero errors across all source projects
- All six implementation phases completed (theme infra, dark polish, light themes, dashboard, page cleanup, global polish)
- Manual QA across themes confirmed

## Lessons learned

- `color-mix()` CSS function works reliably in MAUI Blazor's Edge WebView — safe to use for dynamic color blending (e.g., active nav backgrounds)
- `FluentDesignTheme` mode switching (`Dark`/`Light`) coordinates cleanly with custom CSS variable overrides when layered via `data-theme` attribute selectors
- Theme init must happen in `OnAfterRenderAsync(firstRender)` in MAUI Blazor Hybrid (BL-6 pitfall) — cannot use `OnInitializedAsync` for JS interop
- Shipping all palette variants costs negligible CSS weight and avoids opinionated restrictions on user preference

## Follow-up

- **ServiceBusPage inline-style migration** — CSS classes (`sb-page-shell`, `sb-entity-pane`, `sb-detail-pane`) already exist in `app.css`; Razor markup not yet updated
- **RedisPage key-type badge cleanup** — `.key-type-badge--*` classes exist in `app.css`; Razor markup not yet updated
- **Full accessibility audit** — Color contrast ratios across all themes not formally verified; tracked separately

## Archive metadata

- Feature folder: `docs/features/archive/ui-ux-revamp/`
- Primary files changed: `app.css`, `MainLayout.razor`, `NavItem.razor`, `TopBar.razor`, `StatusBar.razor`, `DashboardPage.razor`, `SettingsPage.razor`, `AppearanceSettings.razor`, `ReleasesPage.razor`, `StoragePage.razor`, `AksPage.razor`, `PodGrid.razor`
- No backend or domain model changes
