# Feature Overview - responsive-layout

---

title: "Feature Overview - responsive-layout"
owner: ""
status: "Planned"
jira: ""
created: "2026-04-18"
updated: "2026-04-18"

---

## Goal

Make the SwebKit shell and all pages adapt gracefully to a range of desktop window sizes — from a narrow 1024 px laptop up to 4 K — without clipping content, squishing data-dense panels, or breaking grid layouts.

## Value

SwebKit is a MAUI Blazor Hybrid desktop app. Users resize the window frequently, run it beside other tools, or use it on Surface devices at 1366×768 / 125 % DPI. The current layout has no CSS breakpoints, several fixed-width panels, and grids that never wrap. Below roughly 1100 px the UI clips silently. This feature closes that gap so the app is genuinely usable across the full desktop size range.

## Scope

**In scope:**

- Shell-level CSS breakpoints and a minimum usable width contract
- Automatic nav collapse below a defined breakpoint (instead of requiring user toggle)
- Dashboard hero row wrapping at narrow widths
- Multi-panel pages (Service Bus, AKS, Redis, Storage) — panel min-width safety and layout adaptation at narrow windows
- Overlay max-width safety (command palette 580 px, keyboard shortcuts 620 px, notification toast 340 px, modals)
- Settings and config form layout — single-column at narrow widths
- Incident Timeline, Observability, and Pipelines pages — panel clipping and toolbar wrapping
- Adding a `--shell-min-width` CSS variable and ensuring `html` never clips below it

**Out of scope:**

- Mobile or tablet (touch) optimisation — SwebKit is desktop-only
- Landscape/portrait orientation handling (desktop windows do not rotate)
- Changing any page feature logic, domain data, or backend behaviour
- Adding new pages or sections
- Per-app-window custom layout persistence

## Dependencies

- CSS custom property system (`app.css` `:root` block) — all layout dimensions already use tokens; breakpoints extend this pattern
- `MainLayout.razor` — shell grid driver; auto-collapse hook goes here
- MAUI Blazor WebView rendering pipeline — CSS media queries work inside the WebView; no MAUI-specific workarounds needed
- Pitfall file: `docs/pitfalls/blazor-maui.md` — BL-1 to BL-5 apply to any component changes

## Risks & mitigations

- Risk: Auto-collapsing the nav at a breakpoint may be unexpected for users who set it manually — Mitigation: only auto-collapse on initial render below the breakpoint; user toggle overrides and is remembered in `UiStateRepository`
- Risk: Panel min-width changes in Service Bus may affect scroll behaviour inside the entity tree — Mitigation: test at 1024 px, 1280 px, and 1920 px before marking wave done
- Risk: Fixed-width overlays (`width: 580px`) break at very narrow windows — Mitigation: clamp to `min(580px, calc(100vw - 48px))` so they stay within viewport
- Risk: CSS Grid auto-fit changes on the dashboard alter visual density at wide screens — Mitigation: keep a `max` track size so tiles do not grow indefinitely on 4 K

## Waves

| Wave | Scope | Effort |
|------|-------|--------|
| 0 | Shell tokens + breakpoint foundation | Low |
| 1 | Dashboard grid wrapping | Low |
| 2 | Service Bus, AKS, Redis, Storage panel safety | Medium |
| 3 | Overlay max-width safety | Low |
| 4 | Settings and config forms | Low |
| 5 | Incident Timeline, Observability, Pipelines | Medium |

## Related documents

- Architecture: `docs/architecture/architecture.md`
- Design: `docs/architecture/design.md`
- Codebase guide: `docs/architecture/codebase-guide.md`
- Pitfalls: `docs/pitfalls/blazor-maui.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation: `frontend.md`
