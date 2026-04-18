# Status - responsive-layout

---

title: "Status - responsive-layout"
owner: ""
state: "Planned"
jira: ""
branch: ""
started: ""
last_updated: "2026-04-18"

---

## Quick summary

Second-pass audit complete. Plan significantly corrected: the app is substantially more responsive than initially assessed. Most "Wave 3 overlay" items and all of "Wave 1 dashboard" are already implemented. Remaining real work is much smaller.

**Jira:** not linked

**Current focus:** Wave 3 (toast only) — add one-line `max-width` to `NotificationToast.razor.css`. Then Wave 0 (shell breakpoint + auto-collapse).

## Second-audit findings (2026-04-18)

Already responsive — **do not touch**:
- `.command-palette` — already has `width: 580px; max-width: calc(100vw - 32px)` ✅
- `.shortcuts-panel` — already has `width: 620px; max-width: calc(100vw - 32px)` ✅
- `.top-bar-popover--workspace` — already has `min(360px, calc(100vw - 32px))` ✅
- `.tab-bar` (global) — already has `overflow-x: auto` ✅
- `.aks-tab-bar`, `.aks-network-tab-bar` — already have `overflow-x: auto` ✅
- `.mdp-header`, `.mdp-actions` — already have `flex-wrap: wrap` ✅
- `.entity-tree-action-buttons` — already has `flex-wrap: wrap` ✅
- `DashboardPage.razor.css` — already has `@media` breakpoints at 480/800/1100 px ✅

Regression risk — **never add CSS `min-width` to `.resizable-panel`**. The JS drag handler uses inline `min-width`. Use the `MinWidth` parameter at the call site instead. Both existing call sites are already safe (AksDetailPanels: 320, ServiceBusPage: 240).

## Progress checklist

### Wave 0 — Shell token + breakpoint foundation
- [ ] Add `--shell-min-width: 900px` and breakpoint variables to `:root` in `app.css`
- [ ] Add `@media (max-width: 1100px)` shell grid rule (collapsed nav, tighter padding)
- [ ] Auto-collapse nav on initial render below breakpoint in `MainLayout.razor`
- [ ] Verify nav user-toggle still overrides auto-collapse state

### ~~Wave 1 — Dashboard grid wrapping~~ ✅ Already done
- [x] Dashboard hero row already has `@media (max-width: 800px) { grid-template-columns: repeat(2, 1fr); }` — no changes needed

### Wave 2 — Data-dense page panel safety
- [ ] Service Bus: clamp entity panel to `min(var(--sb-entity-panel-width), 40vw)`
- [ ] Service Bus: clamp detail drawer to `min(var(--sb-detail-drawer-width), 50vw)`
- [ ] AKS: verify toolbar rows wrap at narrow widths (AksConnectionBar already has `overflow-x: auto`)
- [ ] Redis: verify key-list panel clamps correctly
- [ ] Storage: verify container-list panel clamps correctly
- [ ] Visual test all four pages at 1024 px, 1280 px, 1920 px

### Wave 3 — Overlay max-width safety (toast only)
- [x] ~~Command palette~~ — already has `max-width: calc(100vw - 32px)` — **no change needed**
- [x] ~~Keyboard shortcuts panel~~ — already has `max-width: calc(100vw - 32px)` — **no change needed**
- [ ] Notification toast: add `max-width: calc(100vw - 32px)` to `.nt-toast` in `NotificationToast.razor.css` (one-line fix)
- [ ] Any modal dialogs: add `max-width: calc(100vw - 48px)` guard if generic modals exist

### Wave 4 — Settings and config forms
- [ ] Settings page: switch to single-column layout below 1100 px
- [ ] Config forms (ServiceBusConfigForm, AksConfigForm, RedisConfigForm, etc.): verify field rows wrap correctly

### Wave 5 — Incident Timeline, Observability, Pipelines
- [ ] Incident Timeline toolbar: verify wrapping at 1024 px
- [ ] Incident Timeline side panels: clamp widths
- [ ] Observability tabs: verify overflow behaviour
- [ ] Pipelines page: verify tree + detail split at narrow widths

## Completed

- Second-pass audit — found command palette, keyboard shortcuts panel, all tab bars, dashboard grid, and entity action buttons already responsive
- Initial audit + feature plan creation (Wave 0–5 structure)

## Remaining

- Wave 0: Shell breakpoint + auto-collapse (most complex)
- Wave 3 (toast only): 1-line `max-width` fix in `NotificationToast.razor.css`
- Wave 2: Service Bus / AKS / Redis / Storage panel clamp verification
- Waves 4–5: Settings forms, Incident Timeline, Observability, Pipelines

## Blockers

- None

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- SwebKit targets Windows desktop only — no mobile or tablet breakpoints needed
- MAUI Blazor WebView respects CSS `@media` queries inside the WebView host correctly
- All dimension changes should prefer CSS custom property overrides (`var(--...)`) over hard-coded pixel overrides so themes continue to work
