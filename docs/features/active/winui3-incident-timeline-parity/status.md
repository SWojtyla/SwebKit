# Status - winui3-incident-timeline-parity

---

title: "Status - winui3-incident-timeline-parity"
owner: ""
state: "Review"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-27"
last_updated: "2026-04-27"

---

## Quick summary

The WinUI Incident Timeline slice now includes a native workbench, a native Settings mapping editor, dashboard and cutover-copy alignment, and source-page drill-through from native Service Bus, Pipelines, and Observability surfaces. Focused WinUI launch coverage now exists for all three native source pages, and the remaining follow-up is broader integration review rather than feature-local implementation debt.

**Jira:** not linked

**Current focus:** hold the delivered WinUI parity slice in review while broader integration validation stays on the cheaper focused-test path.

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
- Added focused WinUI test coverage for the new Service Bus, Pipelines, and Observability investigation-launch commands.
- Recorded the WinUI validation-cadence pitfall: use `build-winui` as the inner-loop executable gate, and reserve filtered `dotnet test --no-build` for the final pass.
- Re-ran the focused WinUI Incident Timeline launch slice after the Pipelines coverage addition and kept the targeted files green.

## Remaining

- No blocking feature-local implementation work remains.
- Broader manual cutover review can still exercise the shipped native route, but it is no longer source-level debt inside this slice.

## Blockers

- None for implementation. The only active process constraint is to avoid raw WinUI `dotnet test` during the inner loop because it is too expensive for small local edits.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: `build-winui` passes. Focused WinUI validation across `ServiceBusPageViewModelTests`, `ObservabilityPageViewModelTests`, and `ReadinessStateViewModelTests` now passes `30/30`, covering the native Service Bus, Pipelines, and Observability investigation-launch paths.

## Notes

- This feature exists because the current shell, settings, and dashboard state now make the missing Incident Timeline parity slice visible instead of hidden behind PlaceholderPage or cutover copy.
