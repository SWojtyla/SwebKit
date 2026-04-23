# Frontend Plan - responsive-layout

---

title: "Frontend Plan - responsive-layout"
owner: ""
status: "Not started"

---

## Goal

Every visible surface in SwebKit adapts cleanly between 1024 px and 4 K window widths with no content clipping, no overflowing fixed-width containers, and readable information density at all sizes.

---

## Evaluation findings

> **Second-pass audit corrections (2026-04-18):** Several items reported in the first audit are already handled. See the "Already responsive — do not touch" section below before acting on any wave.

The audit found the following remaining problems. These drive the wave plan below.

### Shell grid — no breakpoints

`app.css` has **zero** `@media` rules in the main shell/global stylesheet (`app.css`). Scoped component CSS files (e.g. `DashboardPage.razor.css`) may have their own breakpoints. The shell uses:

```css
.app-shell {
    grid-template-columns: var(--nav-width) 1fr;   /* 240px + fluid */
}
.app-shell.nav-collapsed {
    grid-template-columns: var(--nav-collapsed-width) 1fr;  /* 56px + fluid */
}
```

There is no automatic collapse or layout shift at any window width. The nav remains 240 px until the user manually collapses it.

### ~~Dashboard hero row — fixed 4-column grid~~ ✅ ALREADY DONE

`DashboardPage.razor.css` already has three `@media` breakpoints:

```css
@media (max-width: 1100px) {
    .dashboard-lower { grid-template-columns: 1fr; }
}
@media (max-width: 800px) {
    .dashboard-hero-row { grid-template-columns: repeat(2, 1fr); }
}
@media (max-width: 480px) {
    .dashboard-hero-row { grid-template-columns: 1fr; }
}
```

**Wave 1 (dashboard) is already implemented. Do not rewrite it.**

### Service Bus multi-pane layout

Three panels plus nav must share the window width:
- Nav (collapsed): 56 px
- Entity panel: `--sb-entity-panel-width: 260px` (fixed)
- Detail drawer: `--sb-detail-drawer-width: 380px` (fixed)
- Main message list: remaining `1fr`

Total minimum before main content starts = **696 px**. At 1024 px the message list is only 312 px wide. At narrower windows the main panel can collapse to zero.

### Fixed-width overlays

| Overlay | Fixed width | Status |
|---------|-------------|--------|
| Command palette | `width: 580px` in `app.css` | ✅ Already has `max-width: calc(100vw - 32px)` — **do not touch** |
| Keyboard shortcuts panel | `width: 620px` in `app.css` | ✅ Already has `max-width: calc(100vw - 32px)` — **do not touch** |
| Notification toast | `width: 320px` in `NotificationToast.razor.css` `.nt-toast` | ⚠️ No `max-width` clamp — needs one line fix |

**Note:** The `width: 340px` at `app.css` line 2553 is `.details-pane` (a flex side panel), not a toast. Do not confuse the two.

### Modals (config forms, detail dialogs)

Modal dialogs use `width: 580px` or similar. No `max-width: calc(100vw - N)` guard. Not a daily problem at normal desktop widths but clips on Surface at high DPI/125 %.

---

## Already responsive — do not touch

These elements were audited and are already responsive. Do not add redundant rules.

| Element | File | How it's responsive |
|---------|------|---------------------|
| `.command-palette` | `app.css` ~line 2132 | `width: 580px; max-width: calc(100vw - 32px)` |
| `.shortcuts-panel` | `app.css` ~line 2308 | `width: 620px; max-width: calc(100vw - 32px); max-height: calc(100vh - 120px)` |
| `.top-bar-popover--workspace` | `app.css` | `width: min(360px, calc(100vw - 32px))` |
| `.tab-bar` (global) | `app.css` | `overflow-x: auto; scrollbar-width: none` |
| `.aks-tab-bar`, `.aks-network-tab-bar` | `AksConnectionBar.razor.css` | `overflow-x: auto` |
| `.mdp-header`, `.mdp-actions` | `MessageDetailPane.razor.css` | `flex-wrap: wrap` |
| `.entity-tree-action-buttons` | `app.css` | `flex-wrap: wrap` |
| `.dashboard-hero-row`, `.dashboard-lower` | `DashboardPage.razor.css` | `@media` breakpoints at 480/800/1100 px |

## Regression risks

### ResizablePanel — CSS min-width must NOT be overridden

`ResizablePanel.razor` uses inline style `style="width:@(Width)px; min-width:@(MinWidth)px"`. The JS drag handler recalculates width relative to this inline min-width. Adding any CSS min-width rule for `.resizable-panel` will break the drag math.

**Existing call sites already have safe values:**
- `AksDetailPanels.razor`: `MinWidth="320"` — safe
- `ServiceBusPage.razor`: `MinWidth="240"` — safe

**Rule:** Never add CSS `min-width` to `.resizable-panel` or its wrappers. Change `MinWidth` parameter at the call site only if adjustment is needed.

### Theme system — 12 `[data-theme='...']` blocks

All CSS custom property tokens are duplicated across 12+ theme blocks in `app.css`. Any structural token rename or addition must be applied in every theme block, not just `:root`.

---

## Impacted areas

### Wave 0 — Shell token + breakpoint foundation

| File | Change |
|------|--------|
| `src/SwebKit.App/wwwroot/app.css` — `:root` | Add `--shell-min-width: 900px`, `--breakpoint-narrow: 1100px`, `--breakpoint-wide: 1600px` |
| `src/SwebKit.App/wwwroot/app.css` — `.app-shell` | Add `@media (max-width: 1100px)` rule: collapse to icon-only nav column, tighter top-bar padding |
| `src/SwebKit.App/Components/Layout/MainLayout.razor` | On `OnAfterRenderAsync` first render, if `window.innerWidth < 1100` set `IsNavExpanded = false` (do not overwrite if user has a persisted state in `UiStateRepository`) |

**Breakpoint rule pattern:**
```css
@media (max-width: 1100px) {
    .app-shell {
        grid-template-columns: var(--nav-collapsed-width) 1fr;
    }
}
```

Note: `MainLayout` still tracks `IsNavExpanded` for the CSS class. The media query overrides the _column width_ but the class for icon labels (`nav-collapsed`) must also be set. The cleanest approach is to detect window width in `OnAfterRenderAsync` using a JS interop call and set the `IsNavExpanded` field to `false` if below threshold. The toggle remains fully functional thereafter.

JS interop needed: `window.innerWidth` read — already available from `keyboardShortcuts.js` pattern; add a small helper in `src/SwebKit.App/wwwroot/js/shellHelpers.js` (or extend `keyboardShortcuts.js`).

### Wave 1 — ~~Dashboard grid wrapping~~ ✅ ALREADY DONE

All three breakpoints already exist in `DashboardPage.razor.css` (lines 547–563). No changes needed.

### Wave 2 — Service Bus, AKS, Redis, Storage panel safety

**Service Bus — file:** `src/SwebKit.App/wwwroot/app.css` (sb-entity-panel, sb-detail-drawer sections)

Current:
```css
:root {
    --sb-entity-panel-width: 260px;
    --sb-detail-drawer-width: 380px;
}
```

Change to clamp in the panel rules themselves (keep variables as their "preferred" size):
```css
.sb-entity-panel {
    width: min(var(--sb-entity-panel-width), 38vw);
    min-width: 180px;
}
.sb-detail-drawer {
    width: min(var(--sb-detail-drawer-width), 46vw);
    min-width: 220px;
}
```

At 1024 px (nav collapsed = 56 px + entity 260 px + drawer 380 px = 696 px leaving 328 px for main) this is tolerable. But to be safe, collapse the detail drawer to its collapsed state automatically at `< 1280px` or let the existing `sb-entity-panel-collapsed-width: 48px` pattern drive it.

**AKS page:** `AksConnectionBar.razor.css` already uses `overflow-x: auto` — preserve this. Verify that `AksDetailPanels` does not have a hardcoded minimum that causes clipping. Check `AksDetailPanels.razor.css` side-panel width.

**Redis / Storage:** Survey `RedisPage.razor.css` and `StoragePage.razor.css` for fixed panel widths. Apply same `min(preferred, Xvw)` clamp pattern.

Key file list:
- `src/SwebKit.App/wwwroot/app.css` (sb panel rules)
- `src/SwebKit.App/Components/Aks/AksDetailPanels.razor.css`
- `src/SwebKit.App/Components/Redis/` — CSS files
- `src/SwebKit.App/Components/Storage/` — CSS files

### Wave 3 — Overlay max-width safety

Only the notification toast needs a fix. Command palette and keyboard shortcuts panel are already responsive (see no-touch list above).

**Notification toast** — file: `src/SwebKit.App/Components/Notifications/NotificationToast.razor.css`

Class: `.nt-toast`. Currently has `width: 320px`. The container is `position: fixed; right: 16px`, so on windows narrower than ~352 px the toast overflows.

```css
/* Before */
.nt-toast {
    width: 320px;
}

/* After */
.nt-toast {
    width: 320px;
    max-width: calc(100vw - 32px);
}
```

This is a one-line addition — the smallest change in the whole plan. Apply with no other changes to that file.

**Generic modal rule** — add to `app.css` global modal selector (if any modals exist):
```css
.modal-dialog, .modal-container {
    max-width: calc(100vw - 48px);
    max-height: calc(100vh - 64px);
}
```

### Wave 4 — Settings and config forms

| File | Change |
|------|--------|
| `src/SwebKit.App/Components/Pages/SettingsPage.razor.css` | At < 1100 px switch two-column settings layout to single-column |
| `src/SwebKit.App/Components/Pages/ServiceBusConfigForm.razor.css` etc. | Ensure form field rows use `flex-wrap: wrap` so label + input stack vertically |

Pattern for config form rows:
```css
.config-form-row {
    display: flex;
    flex-wrap: wrap;
    gap: var(--spacing-md);
    align-items: flex-start;
}
.config-form-label {
    flex: 0 0 140px;
    min-width: 0;
}
.config-form-input {
    flex: 1 1 200px;
    min-width: 0;
}
```

### Wave 5 — Incident Timeline, Observability, Pipelines

**Incident Timeline** (`src/SwebKit.App/Components/IncidentTimeline/`):
- `InvestigationSeedBanner.razor.css` already uses `flex-wrap: wrap` — good.
- `MappingProposalPanel.razor.css` has `min-width: 0` — good.
- Toolbar items may overflow at narrow windows; add `flex-wrap: wrap` to the toolbar container.
- Side panels: apply same `min(preferred, Xvw)` clamp as Service Bus.

**Observability** (`src/SwebKit.App/Components/Observability/`):
- Tab row — verify overflow behaviour; add `overflow-x: auto` to tab container if absent.
- Chart/log areas — confirm they use `width: 100%` relative sizing.

**Pipelines** (`src/SwebKit.App/Components/Pipelines/`):
- Tree + detail split — clamp tree panel width.
- Filter row — verify `flex-wrap: wrap`.

---

## UX notes

- **Nav auto-collapse**: only happens on initial render below 1100 px. After that, the toggle button controls it. A persisted `IsNavExpanded = false` in `UiStateRepository` is honoured over the breakpoint default.
- **Panel clamping**: the `min(preferred, Xvw)` approach preserves the design at normal widths and degrades gracefully. Do not collapse panels to zero — enforce `min-width` floors so content remains readable.
- **Dashboard grid wrapping**: at 900–1100 px the hero row should show 2×2. At > 1100 px it stays 4-in-a-row. At > 1600 px it remains 4-in-a-row but stat cards are wider.
- **Scroll preservation**: all scroll containers (`overflow-y: auto`) remain unchanged. Internal page scroll is the correct pattern for desktop.

## Tasks

- [ ] Wave 0: add breakpoint tokens + shell media query + auto-collapse logic
- [x] ~~Wave 1: dashboard hero row + health grid auto-fit~~ — already done in `DashboardPage.razor.css`
- [ ] Wave 2: panel min-width clamps for Service Bus, AKS, Redis, Storage (ResizablePanel: use `MinWidth` param only, never CSS)
- [ ] Wave 3 (toast only): add `max-width: calc(100vw - 32px)` to `.nt-toast` in `NotificationToast.razor.css`
- [ ] Wave 4: settings + config form row wrapping
- [ ] Wave 5: Incident Timeline / Observability / Pipelines toolbar and panel fixes
- [ ] Cross-cut: visual regression check at 1024, 1280, 1366, 1920, 2560 px

## Validation

See `test-plan.md`.

## Notes

- All CSS changes should stay in the existing file-per-component scoped CSS or in `app.css`. Do not introduce a new global CSS file.
- The design token pattern (`var(--...)`) must be preserved for all changed values so themes continue to apply correctly.
- The MAUI WebView on Windows respects `@media (max-width: N)` queries — this has been verified with the existing app structure.
- Window resize events in Blazor: `IJSRuntime` can listen to `window.resize` if dynamic re-evaluation is needed, but CSS media queries alone handle the layout changes without JS intervention (JS is only needed for the initial nav-collapse state on first render).
