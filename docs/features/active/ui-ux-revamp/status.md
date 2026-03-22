# Status — ui-ux-revamp

---

title: "Status — ui-ux-revamp"
owner: ""
state: "Proposed"
branch: ""
started: ""
last_updated: "2026-03-22"

---

## Quick Summary

Feature planning complete. Palette decision made: all four light palettes (Azure Bloom, Coral Studio, Forest Dev, Violet Cloud) are supported as selectable themes. Implementation not yet started.

**Current focus:** Phase 1 — CSS variable infrastructure.

## Progress Checklist

### Planning

- [x] Feature folder created
- [x] `index.md` written
- [x] `frontend.md` written (detailed phase plan)
- [x] Palette decision recorded in `decisions.md` — all four light palettes supported

### Phase 1 — Theme Infrastructure

- [ ] `app.css` restructured: `:root` → `[data-theme="dark"]` defaults + four `[data-theme="light-*"]` blocks
- [ ] New design tokens added: `--shadow-sm/md/lg`, `--radius-sm/md/lg`
- [ ] Typography base bumped to 14px
- [ ] `MainLayout.razor` wired to read/write theme via `localStorage` JSInterop
- [ ] `SettingsPage` gains "Appearance" section with theme dropdown
- [ ] `FluentDesignTheme` mode parameter driven from stored preference
- [ ] Theme toggle verified: dark ↔ four light themes switch correctly, persists across app restart

### Phase 2 — Dark Theme Polish + Nav Icon Colors

- [ ] `TopBar.razor` hardcoded `#1E1E2E` replaced with CSS variable
- [ ] `NavItem.razor` accepts `Area` prop and emits `data-area` attribute
- [ ] Per-feature icon color CSS rules added to `app.css`
- [ ] Dark theme visual QA pass: top bar, status bar, nav all have distinct surface levels
- [ ] Micro-animation: nav hover transition added

### Phase 3 — Light Themes

- [ ] All four `[data-theme="light-*"]` blocks added to `app.css`
- [ ] `FluentDesignTheme` switches to `DesignThemeModes.Light` for any non-dark theme
- [ ] All five options (Dark + 4 light) available in Appearance settings dropdown
- [ ] QA pass on each light theme: all pages legible, no hardcoded dark color leaks

### Phase 4 — Dashboard Redesign

- [ ] `DashboardPage.razor` hero stats row added
- [ ] Health tile grid upgraded: larger tiles, accent borders per feature
- [ ] Lower content reworked: 2-column (activity feed + pinned side by side)
- [ ] `DashboardPage.razor.css` updated to full-width layout, max-width removed
- [ ] Dashboard QA pass on 1920×1080 and 1440×900

### Phase 5 — Page Inline-Style Cleanup

- [ ] `SettingsPage.razor` — two-column layout, Appearance section added
- [ ] `ReleasesPage.razor` — inline styles → CSS classes, pill-style tab bar
- [ ] `ServiceBusPage.razor` — inline styles → CSS classes, entity pane hierarchy
- [ ] `AksPage.razor` — inline styles → CSS classes, pod status color badges
- [ ] `RedisPage.razor` — layout and spacing cleanup
- [ ] `StoragePage.razor` — blob browser layout cleanup

### Phase 6 — Global Style Polish

- [ ] Shadow variables applied across cards and overlays
- [ ] Border-radius tokens applied consistently across all components
- [ ] Micro-animations: button press, card expand, command palette open
- [ ] `StatusBar.razor` — informational improvements
- [ ] Final full-app visual QA pass

### Validation

- [ ] All pages verified in dark theme
- [ ] All pages verified across all four light themes
- [ ] Theme preference persists after app restart
- [ ] No regression in existing component tests
- [ ] Manual accessibility spot-check: color contrast ratios

## Completed

_(none yet)_

## Remaining

- Phase 1–6 implementation (see checklist above)

## Blockers

_(none — palette decision resolved)_

## Validation

- No automated test plan (visual / CSS changes only).
- Manual QA checklist embedded in each phase above.
- Component tests in `SwebKit.App.Tests` should continue to pass after Razor structural changes.

## Notes

- Phase 1 is the only prerequisite for all other phases; all other phases can be worked independently once Phase 1 is done.
- Phase 2 (dark polish + nav colors) is the fastest visible win and can ship before the light theme is ready.
