# Feature Overview - winui3-incident-timeline-parity

---

title: "Feature Overview - winui3-incident-timeline-parity"
owner: ""
status: "In Progress"
jira: "not linked"
created: "2026-04-27"
updated: "2026-04-27"

---

## Goal

Finish the native Incident Timeline parity slice so the `incident-timeline` WinUI route becomes a usable investigation workspace with native settings repair, honest shell copy, and native source-page drill-through that matches the route now being advertised in the WinUI cutover shell.

## Value

The WinUI shell already exposes Incident Timeline in navigation and now routes `incident-timeline` to a dedicated native page, but the current page was only a stub, native Settings deferred the area, and the dashboard still said Incident Timeline was outside the current cutover scope. Closing this slice removes one of the clearest remaining WinUI migration gaps, restores direct operator drill-through from native source pages, and keeps the native shell from promising a route that is not operational.

## Scope

- In scope: replace the current native Incident Timeline stub with a real WinUI workbench for workload scope selection, manual refresh, source coverage, evidence list/detail, and the main empty/loading/error states already established in MAUI.
- In scope: add native Settings > Incident Timeline coverage for workload mappings and the related repair path so page-level guidance can land on a real native settings surface.
- In scope: align dashboard and cutover-facing copy so Incident Timeline is no longer described as outside the WinUI migration scope once the route has a real native owner.
- In scope: native investigation-seed launch from WinUI Service Bus, Pipelines, and Observability surfaces when the route contract already exists and the equivalent MAUI caller evidence is present.
- In scope: focused WinUI validation for route navigation, settings or mapping repair flow, representative workbench states, and copy alignment.
- Out of scope: backend evidence-source redesign or new Incident Timeline capabilities beyond the current MAUI baseline.
- Out of scope: new Incident Timeline launch sources beyond the currently implemented Service Bus, Pipelines, and Observability entry points.

## Source surfaces

- MAUI baseline: `src/SwebKit.App/Components/Pages/IncidentTimelinePage.razor`, `src/SwebKit.App/Components/Pages/IncidentTimelineConfigForm.razor`, `src/SwebKit.App/Components/Pages/SettingsPage.razor`, `src/SwebKit.App/Services/IncidentInvestigationLauncher.cs`
- Current WinUI surface: `src/SwebKit.WinUI/MainWindow.xaml`, `src/SwebKit.WinUI/MainWindow.xaml.cs`, `src/SwebKit.WinUI/Views/IncidentTimeline/IncidentTimelinePage.xaml`, `src/SwebKit.WinUI/Views/Settings/SettingsPage.xaml`, `src/SwebKit.WinUI/ViewModels/Settings/SettingsViewModel.cs`, `src/SwebKit.WinUI/ViewModels/Dashboard/DashboardPageViewModel.cs`

## Dependencies

- Related active feature: `docs/features/active/winui3-cutover-audit-hardening/`
- Functionality baseline: `docs/architecture/functionalities/incident-timeline.md`
- Pitfall files that apply: `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`
- Relevant automated surface: `tests/SwebKit.WinUI.Tests/`

## Parallel execution contract

- This feature owns the native `incident-timeline` route, any new WinUI Incident Timeline view models or controls, the Incident Timeline section in native Settings, and incident-specific dashboard or cutover copy alignment.
- It may consume the existing configuration repositories, configuration-health actions, and shared shell primitives without reopening backend aggregation rules already used by the MAUI workbench.
- Only reusable shell primitive gaps or broader cutover copy policy changes should leave this slice.

## Risks & mitigations

- Risk: the native route stays a shell stub while the shell continues to advertise parity.  
  Mitigation: treat route workbench completion, not just route ownership, as the feature bar.
- Risk: page guidance links to a native settings section that still does not exist.  
  Mitigation: land the settings or mapping repair path in the same slice as the page workbench.
- Risk: implementation quietly expands into speculative native source-page launch hooks.  
  Mitigation: keep native drill-through limited to the WinUI source pages with clear MAUI precedent and an existing typed seed contract.

## Related documents

- Cutover umbrella: `docs/features/active/winui3-cutover-audit-hardening/`
- Incident Timeline functionality: `docs/architecture/functionalities/incident-timeline.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: none yet
