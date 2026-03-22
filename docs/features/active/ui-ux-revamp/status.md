# Status — ui-ux-revamp

---

title: "Status — ui-ux-revamp"
owner: ""
state: "Done"
branch: "main"
started: "2026-03-22"
last_updated: "2026-03-22"

---

## Quick Summary

All six phases implemented in a single pass. Theme infrastructure, dark-theme polish, all four light themes, dashboard redesign, per-page inline-style cleanup, and global token application are complete.

**Current focus:** Validation / QA pass.

## Progress Checklist

### Planning

- [x] Feature folder created
- [x] `index.md` written
- [x] `frontend.md` written (detailed phase plan)
- [x] Palette decision recorded in `decisions.md` — all four light palettes supported

### Phase 1 — Theme Infrastructure

- [x] `app.css` restructured: `:root` holds layout/spacing/typography/radius/shadow constants; `[data-theme="dark"]` holds color tokens; four `[data-theme="light-*"]` blocks added
- [x] New design tokens added: `--shadow-sm/md/lg`, `--radius-sm/md/lg`
- [x] Typography base bumped to 14px; `--font-size-sm` → 12px; `--font-size-md` → 14px
- [x] `MainLayout.razor` reads theme from `localStorage` in `OnAfterRenderAsync(firstRender)` and exposes `SetThemeAsync()` + `CurrentTheme` as a `CascadingValue<MainLayout>` named `"Layout"`
- [x] `SettingsPage` gains two-column layout with "Appearance" section as first nav item
- [x] `AppearanceSettings.razor` component created in `Components/Shared/` with `FluentSelect` theme dropdown
- [x] `FluentDesignTheme` mode parameter driven from `_currentTheme != "dark"`
- [x] Theme toggle wiring: dark ↔ four light themes switch via `SetThemeAsync`, persisted to `localStorage`

### Phase 2 — Dark Theme Polish + Nav Icon Colors

- [x] `TopBar.razor` hardcoded `#1E1E2E` removed; all inline colours replaced with CSS variables (`--env-text`, `--color-surface-2`, `--color-border`, etc.)
- [x] `NavItem.razor` emits `data-area="@Area"` attribute
- [x] Per-feature icon color CSS rules added to `app.css` (all 7 areas, active + inactive states, `color-mix()` backgrounds)
- [x] Dark theme surfaces differentiated: `--color-surface-3` added for top bar / status bar
- [x] Micro-animation: nav hover transition added (`border-left-color`, `background`, `color`)

### Phase 3 — Light Themes

- [x] All four `[data-theme="light-*"]` blocks added to `app.css`
- [x] `FluentDesignTheme` switches to `DesignThemeModes.Light` for any non-dark theme
- [x] All five options (Dark + 4 light) available in Appearance settings dropdown

### Phase 4 — Dashboard Redesign

- [x] `DashboardPage.razor` hero stats row added (4 stat chips with FluentIcon, value, label)
- [x] Health tile grid upgraded: `health-tile-wrap` divs with feature-colored left borders + `box-shadow`
- [x] Lower content reworked: `dashboard-lower` 2-column grid (activity feed 2/3 + pinned 1/3)
- [x] `DashboardPage.razor.css` updated: `max-width: 960px` removed, full-width `padding`-based layout, responsive breakpoints retained

### Phase 5 — Page Inline-Style Cleanup

- [x] `SettingsPage.razor` — two-column `settings-shell` layout; `FluentAccordion` replaced with sidebar nav + `switch` content well; Appearance section first
- [x] `ReleasesPage.razor` — inline styles → CSS classes (`page-content`, `page-header`, `page-title`, `release-selector`, `pill-tab-bar`, `pill-tab`, `pill-badge`); delete confirm dialog uses `--shadow-lg` and radius tokens
- [x] `StoragePage.razor` — inline styles → CSS classes (`storage-page-shell`, `storage-account-bar`, `storage-body`, `storage-container-pane`, `storage-blob-pane`, `storage-not-configured`, `storage-select`)
- [x] `AksPage.razor` / `PodGrid.razor` — pod phase column uses `pod-status-badge pod-status-badge--@phase` classes defined in `app.css`
- [ ] `ServiceBusPage.razor` — inline styles → CSS classes (planned; `sb-page-shell`, `sb-entity-pane`, `sb-detail-pane` classes already in `app.css`)
- [ ] `RedisPage.razor` — key type badges cleanup (`.key-type-badge--*` classes in `app.css` ready)

### Phase 6 — Global Style Polish

- [x] Shadow variables applied: `command-palette`, `ctx-menu`, `pill-tab.active`, `surface-card`, `health-tile-wrap`, `dashboard-activity-panel`, `dashboard-pinned-panel`
- [x] Border-radius tokens applied across `app.css` global classes (hardcoded px → `var(--radius-*)`)
- [x] Micro-animations: button press (`transform: scale(0.97)`), command palette overlay `fadeIn`, nav hover transition
- [x] `StatusBar` uses `var(--color-surface-3)` instead of `var(--color-surface)` for visual differentiation
- [ ] Final full-app visual QA pass

### Validation

- [ ] All pages verified in dark theme
- [ ] All pages verified across all four light themes
- [ ] Theme preference persists after app restart
- [ ] No regression in existing component tests
- [ ] Manual accessibility spot-check: color contrast ratios

## Completed

- Phase 1: CSS variable infrastructure + theme token system
- Phase 2: Dark theme polish + nav icon colors + TopBar cleanup
- Phase 3: All four light themes wired and selectable
- Phase 4: Dashboard redesign (hero row, health tile wraps, 2-column lower)
- Phase 5: ReleasesPage, StoragePage, AksPage/PodGrid inline-style cleanup
- Phase 6: Shadow/radius tokens, micro-animations, status bar surface fix

## Remaining

- ServiceBusPage inline-style migration (CSS classes already in `app.css`)
- RedisPage key-type badge migration (CSS classes already in `app.css`)
- Visual QA pass across all themes on 1920×1080 and 1440×900

## Blockers

_(none)_

## Notes

- Phase 1 is the foundation; all theme variables cascade via `[data-theme]` on `.app-shell`.
- `color-mix()` is used for active nav item backgrounds — supported in MAUI Blazor's Edge WebView.
- `SettingsPage` uses `[CascadingParameter(Name = "Layout")]` to call `MainLayout.SetThemeAsync()`.
- `AppearanceSettings.razor` is in `Components/Shared/` — ensure `@using SwebKit.App.Components.Shared` is in `_Imports.razor`.
