# Status - winui3-incident-timeline-parity

---

title: "Status - winui3-incident-timeline-parity"
owner: ""
state: "In Progress"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-27"
last_updated: "2026-04-27"

---

## Quick summary

The WinUI Incident Timeline slice now includes a native workbench, a native Settings mapping editor, dashboard and cutover-copy alignment, and source-page drill-through from native Service Bus, Pipelines, and Observability surfaces. The remaining validation gap is to keep final focused WinUI test execution on the cheap path (`build-winui` first, then filtered `dotnet test --no-build` only at the end) instead of re-triggering raw WinUI `dotnet test` inside the implementation loop.

**Jira:** not linked

**Current focus:** finish docs and validation-cadence hardening so the shipped WinUI parity slice stays aligned with the faster inner-loop workflow.

## Progress checklist

- [x] WinUI versus MAUI gap captured from current repo evidence
- [x] Route ownership shift captured: WinUI now routes `incident-timeline` to a dedicated native page instead of generic fallback behavior
- [x] Native Incident Timeline workbench implemented for scope, coverage, evidence list/detail, and page states
- [x] Native Settings > Incident Timeline form implemented for mapping and repair guidance
- [x] Dashboard and cutover copy aligned so Incident Timeline is no longer described as outside scope
- [x] Focused WinUI validation added for navigation, settings repair, and representative workbench behavior
- [x] Native cross-page investigation launch implemented for evidenced WinUI source pages
- [x] Docs aligned after implementation begins

## Completed

- Confirmed that the WinUI shell advertises Incident Timeline in navigation and `MainWindow` now routes `incident-timeline` to `IncidentTimelinePage`.
- Landed the native Incident Timeline workbench in `src/SwebKit.WinUI` with scope selection, manual refresh, source coverage, evidence timeline/detail, mapping guidance, and seed-aware landing state.
- Replaced the deferred native Settings placeholder with an Incident Timeline mapping editor and current-scope seeding path.
- Updated the native dashboard so Incident Timeline is part of the visible WinUI cutover surface.
- Added native drill-through launch into Incident Timeline from WinUI Service Bus, Pipelines, and Observability pages using typed `IncidentInvestigationSeed` navigation parameters.
- Added focused WinUI test coverage for the new Service Bus and Observability investigation-launch commands.
- Recorded the WinUI validation-cadence pitfall: use `build-winui` as the inner-loop executable gate, and reserve filtered `dotnet test --no-build` for the final pass.

## Remaining

- Finish aligning the active feature docs with the shipped WinUI cross-page launch behavior.
- Re-run the focused WinUI test slice with the corrected final-gate command once the implementation loop is closed.

## Blockers

- None for implementation. The only active process constraint is to avoid raw WinUI `dotnet test` during the inner loop because it is too expensive for small local edits.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: `build-winui` passes. Compile-only validation for `tests/SwebKit.WinUI.Tests` also passes once stale `testhost` locks are cleared. Focused WinUI test execution should use a final filtered `dotnet test --no-build` pass rather than raw inner-loop `dotnet test`.

## Notes

- This feature exists because the current shell, settings, and dashboard state now make the missing Incident Timeline parity slice visible instead of hidden behind PlaceholderPage or cutover copy.
