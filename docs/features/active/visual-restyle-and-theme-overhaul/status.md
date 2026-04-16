# Status - visual-restyle-and-theme-overhaul

---

title: "Status - visual-restyle-and-theme-overhaul"
owner: "GitHub Copilot"
state: "Review"
jira: "not linked"
branch: ""
started: "2026-04-15"
last_updated: "2026-04-16"

---

## Quick summary

`Studio Ledger` is now the chosen global dark direction, and the restyle rollout is implemented across the shared shell, AKS, Storage, Service Bus, Releases, Redis, and Observability surfaces that were called out in scope.

**Jira:** not linked

**Current focus:** Fresh-session smoke validation and human visual sign-off before close-out.

## Progress checklist

### Planning

- [x] Create the feature folder and core planning docs
- [x] Capture the current theme, shell, and table constraints
- [x] Define the low-cost comparison strategy for the initial art-direction choice
- [x] Capture feedback that the first pilot was too palette-driven and needs stronger form differentiation
- [x] Review the live pilot and choose the final direction

### Delivery phases

- [x] Phase 1 - In-app art direction pilot (`Command Deck` vs `Studio Ledger`)
- [x] Phase 2 - Choose direction and complete token audit
- [x] Phase 3 - Theme overhaul
- [x] Phase 4 - Shared shell and surface primitives
- [x] Phase 5 - Shared table system
- [x] Phase 6 - Feature-area adoption and cleanup
- [x] Phase 7 - Validation and docs alignment

## Completed

- Read the architecture, design, codebase guide, and relevant pitfall files before planning.
- Verified that theme state is already hosted through `MainLayout`, `app.css`, and `UserSettingsRepository`.
- Identified that table styling is currently split between limited global helpers, page-specific grid CSS, and many inline table/header styles.
- Identified high inline-style density in Storage, Release, Service Bus, and configuration form surfaces that will block a consistent restyle unless consolidated.
- Recorded a phased plan that preserves layout but upgrades themes, surfaces, and table-heavy workflows.
- Chose the first low-cost implementation slice: two live dark pilot directions exposed through Settings and visible on shell chrome, the dashboard, and representative operational surfaces.
- Implemented an initial pilot pass, then captured user feedback that the comparison still reads as new themes rather than new design directions.
- Confirmed the corrected pilot scope now includes AKS in addition to shell chrome, Settings appearance, Dashboard, and Storage.
- Reworked the live pilot around `Command Deck` and `Studio Ledger`, including shared shell chrome, Settings previews, Storage table framing, and AKS toolbar/grid treatments.
- Refined `Studio Ledger` after direct feedback to remove the brown-heavy palette and serif display treatment while keeping its softer rounded contrast with `Command Deck`.
- Pushed `Studio Ledger` further toward a premium finish by deepening shell contrast, refining borders and shadows, and tightening the display typography hierarchy.
- Chose `Studio Ledger` as the global dark direction and removed the stale pilot comparison from the active theme catalog UI.
- Updated the theme host so new sessions and legacy dark-theme values normalize to `Studio Ledger`.
- Removed the blank shared route-page header shell so routed pages now rely on the shell top bar for page context instead of rendering an empty surface.
- Reintroduced page-local pills and actions through a compact support-strip pattern so header cleanup does not remove useful controls.
- Added a future-ready `Studio Ledger` palette alias in the theme selector block so additional colorways can branch from the same design language later.
- Finished the dedicated layout rollout module in `layout.md` so shell work now has a concrete plan instead of living only as broad frontend bullets.
- Validated the current state with a focused Windows MAUI build after the theme-host and header-shell updates.
- Implemented a bounded AKS/shared primitive fix for theme-safe native controls and overlay stacking, including a non-clipping AKS namespace picker, token-driven confirm/port-forward dialogs, and shared control classes applied across AKS toolbar and side-panel inputs/selects.
- Completed the Releases/Observability adoption slice by moving the remaining high-impact inline styling in `ReleaseDetail` and `ReleaseEditor` into isolated CSS, and by adding local `ObservabilityLogs` surface/dialog styling hooks that keep the logs workspace aligned with the current shared token language without touching `app.css`.
- Finished the final Storage/Service Bus visual adoption slice by extracting Blob detail-pane and Service Bus message-list toolbar/filter/menu/rule-builder/save-dialog styling into component-local CSS that consumes the shared `Studio Ledger` table and control tokens instead of inline styles.
- Replaced the remaining explicit theme-unsafe accent/prod foregrounds and ad hoc overlay stack values that were still visible in AKS, Pipelines, and Storage after the broader adoption slices.
- Updated the E2E helper normalization path so legacy dark aliases map cleanly to `Studio Ledger`, and verified the E2E project still builds after the harness changes.
- Completed focused compile/test validation for the restyle seams: workspace diagnostics reported no source errors, the focused app-component regression suite passed (`46/46`), and the E2E test project build passed.

## Remaining

- Rerun the Playwright smoke suite against a freshly launched app session instead of an already-running WebView2/CDP session so theme assertions are not contaminated by stale in-memory shell state.
- Perform a human visual spot-check in `Studio Ledger` plus one light theme before archive/close-out.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: `get_errors` reported no source or test-project diagnostics. The focused App.Tests restyle suite passed (`46/46`) across AKS toolbar/select seams, Service Bus namespace and message-list behavior, Storage download/detail behavior, Template Picker behavior, and Observability guided logs behavior. `dotnet build tests/SwebKit.E2E.Tests/SwebKit.E2E.Tests.csproj -nologo` passed. The attached-session Playwright smoke run still reused an already-running app session that retained stale theme state in memory, so that result is treated as a harness/environment caveat rather than a fresh-code regression.

## Notes

- The user explicitly wants the global layout to stay the same; this plan treats layout geometry as stable.
- Tables and column headers are an explicit priority, not a secondary polish item.
- The current plan is intentionally foundation-first so later page adoption does not reintroduce one-off styling.
- The chosen direction is no longer treated as a pilot in the active UI; future palette work should branch from `Studio Ledger`, not resurrect a second competing shell language.
- Shell-layout sequencing now lives in `layout.md`; use that module as the source of truth for top-bar, left-nav, status-bar, and page-entry rollout steps.
- The feature is implementation-complete and ready for review; the remaining close-out work is a fresh-launch smoke pass plus human visual sign-off.
