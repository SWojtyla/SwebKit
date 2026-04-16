# Status - visual-restyle-and-theme-overhaul

---

title: "Status - visual-restyle-and-theme-overhaul"
owner: "GitHub Copilot"
state: "In Progress"
jira: "not linked"
branch: ""
started: "2026-04-15"
last_updated: "2026-04-16"

---

## Quick summary

`Studio Ledger` is now the chosen global dark direction. The active theme catalog and default theme handling were updated to treat it as the dark foundation, the blank route-page header shell was removed, and the shared theme selector now anticipates future `Studio Ledger` palette variants without reopening the structural design decision.

**Jira:** not linked

**Current focus:** Carry the chosen `Studio Ledger` language through shared shell primitives, keep the theme catalog palette-ready, and continue the shared table/system rollout without reintroducing pilot-only UI.

## Progress checklist

### Planning

- [x] Create the feature folder and core planning docs
- [x] Capture the current theme, shell, and table constraints
- [x] Define the low-cost comparison strategy for the initial art-direction choice
- [x] Capture feedback that the first pilot was too palette-driven and needs stronger form differentiation
- [x] Review the live pilot and choose the final direction

### Delivery phases

- [x] Phase 1 - In-app art direction pilot (`Command Deck` vs `Studio Ledger`)
- [ ] Phase 2 - Choose direction and complete token audit
- [ ] Phase 3 - Theme overhaul
- [ ] Phase 4 - Shared shell and surface primitives
- [ ] Phase 5 - Shared table system
- [ ] Phase 6 - Feature-area adoption and cleanup
- [ ] Phase 7 - Validation and docs alignment

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

## Remaining

- Complete the token audit and decide which additional `Studio Ledger` palettes should actually be exposed in the user-facing catalog.
- Execute the shared shell-primitives pass before touching many feature pages.
- Roll the new table system through the highest-value surfaces first.
- Validate contrast, keyboard focus, resize behavior, and production-state cues after broader adoption.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Focused Windows build succeeded after promoting `Studio Ledger` as the default dark direction and removing the blank route-header shell. One unrelated existing obsolete API warning remains in `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor`.

## Notes

- The user explicitly wants the global layout to stay the same; this plan treats layout geometry as stable.
- Tables and column headers are an explicit priority, not a secondary polish item.
- The current plan is intentionally foundation-first so later page adoption does not reintroduce one-off styling.
- The chosen direction is no longer treated as a pilot in the active UI; future palette work should branch from `Studio Ledger`, not resurrect a second competing shell language.
- Shell-layout sequencing now lives in `layout.md`; use that module as the source of truth for top-bar, left-nav, status-bar, and page-entry rollout steps.
