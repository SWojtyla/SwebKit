# Status - winui3-layout-redesign

---

title: "Status - winui3-layout-redesign"
owner: ""
state: "Review"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-25"
last_updated: "2026-04-25"

---

## Quick summary

The shared WinUI layout contract is now implemented. `PageScaffold` supports compact headers and inline context, the missing shared primitives are in place, Dashboard and Settings are the reference adopters, and shell chrome spacing has been tightened to give more room to task surfaces.

**Jira:** not linked

**Current focus:** finish manual content-density checks on Dashboard and Settings, then hand the shared primitives off to downstream parity features.

## Progress checklist

- [x] MAUI and WinUI layout surfaces compared
- [x] Shared primitive gaps identified (`StateView`, `MetricCard`, `SectionCard`, `DetailPaneHost`)
- [x] Global content-first layout rule documented and reviewed against current shell constraints
- [x] Dashboard moved onto the redesigned primitives
- [x] Settings frame moved onto the redesigned primitives
- [x] Downstream feature handoff documented
- [x] Tests and docs aligned

## Completed

- Confirmed that no separate layout-redesign feature folder already exists in the repo.
- Identified the cross-cutting layout work that is currently buried inside `winui3-cutover-audit-hardening`.
- Recorded the redesign as the first dependency for the remaining WinUI parity work.
- Added `StateView`, `MetricCard`, `SectionCard`, and `DetailPaneHost` under `src/SwebKit.WinUI/Controls/Shared/`.
- Extended `PageScaffold` with compact-header and inline-context options so secondary guidance can move below the title row instead of expanding it.
- Rebuilt Dashboard and Settings on the new compact scaffold and shared section or metric surfaces.
- Tightened shell host, banner, context header, badge, icon-button, and workspace-hub spacing to keep chrome proportional to the work surface.
- Verified the shared layout slice with `build-winui` after each implementation wave and a focused `tests/SwebKit.WinUI.Tests` run.

## Remaining

- Run the manual Dashboard and Settings content-density checks in `test-plan.md` to confirm the top chrome no longer crowds the primary work surface at typical desktop sizes.
- Validate one downstream page adoption during its parity slice so the new shared primitives prove reusable outside the reference pages.

## Blockers

- No implementation blockers remain.
- Manual visual validation is still pending.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: Automated validation complete; manual Dashboard and Settings walkthrough pending.
- `build-winui` succeeded on 2026-04-25 after the shared-primitives slice, Dashboard adoption, Settings adoption, and shell-spacing pass.
- The focused `tests/SwebKit.WinUI.Tests` suite passed on 2026-04-25.

## Notes

- Downstream feature plans should treat the content-first proportion rule as a global constraint, not a page-local preference.
- The intended reuse path is `PageScaffold.IsHeaderCompact` + `ContextContent` for compact headers, `SectionCard` for operator work sections, `MetricCard` for summary tiles, `StateView` for reusable page states, and `DetailPaneHost` for future list/detail pages.
