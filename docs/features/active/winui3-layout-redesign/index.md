# Feature Overview - winui3-layout-redesign

---

title: "Feature Overview - winui3-layout-redesign"
owner: ""
status: "Review"
jira: "not linked"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Redesign the WinUI shell and page layout so every remaining migration feature lands on a consistent, content-first native information architecture instead of page-local XAML patterns.

## Value

The WinUI host already has real routed coverage, but too much of the vertical budget is currently spent on top-of-page header and context chrome while the actual work surfaces stay compressed. The MAUI app expresses a clearer product layout through shared cards, section groupings, and predictable state handling, but the WinUI redesign now also needs an explicit proportion rule: main content comes first, and header noise must earn its space. This feature makes that shared layout language explicit in WinUI before more parity work deepens the drift.

## Scope

### Wave 0 - Layout contract

- Compare the MAUI shell, dashboard, and settings composition with the current WinUI shell and freeze the native layout contract for compact page headers, action rows, workspace sections, list/detail regions, and page states.
- Define a global proportion rule for desktop pages: shell and page-header chrome should consume the minimum stable vertical space, and the main task surface should own the page.
- Decide which repeated WinUI patterns become shared controls versus page-owned composition.

### Wave 1 - Shared primitives

- Add the missing shared primitives that the remaining feature plans depend on: `StateView`, `MetricCard`, `SectionCard`, and `DetailPaneHost`.
- Extend `PageScaffold` only where necessary so page-level spacing, commands, and secondary guidance stop diverging.
- Add compact-header and inline-context options so secondary information can move out of tall top banners and into the work surface where it is actually used.
- Keep `DeferredPageLoadScheduler` as the lifecycle primitive and avoid folding layout work back into page activation logic.

### Wave 2 - Reference adoption

- Move Dashboard and the Settings frame onto the new layout language first so downstream feature work has two reference implementations.
- Reduce the current top-header footprint in the reference pages and verify that lists, dashboards, and operator work surfaces get materially more visible space.
- Align shell host, banner, context header, and workspace hub spacing with the redesigned page structure.

## Out of scope

- Domain-specific feature completion for Service Bus, AKS, Redis, Storage, Pipelines/Releases, or Observability.
- Migrating Incident Timeline or building a generic editor host.
- Theme-token replacement that is purely cosmetic and does not improve layout or parity delivery.

## Global layout rules

- Main content owns the page. Shell chrome and page headers should consume the minimum stable space needed for title, primary actions, and critical live state.
- Secondary context should move inline, collapse, or live beside the active workspace instead of occupying a tall top-of-page info band.
- Desktop pages should use the available width and height for the active task first, especially grids, list/detail views, dashboards, and editors.
- Any additional top-of-page information must justify itself as task-critical; otherwise it should not displace the main workspace.

## Source surfaces

- MAUI layout baseline: `src/SwebKit.App/Components/Layout/`, `src/SwebKit.App/Components/Pages/DashboardPage.razor`, `src/SwebKit.App/Components/Pages/SettingsPage.razor`
- WinUI layout baseline: `src/SwebKit.WinUI/MainWindow.xaml`, `src/SwebKit.WinUI/Controls/Shell/`, `src/SwebKit.WinUI/Controls/Shared/`, `src/SwebKit.WinUI/Views/Dashboard/`, `src/SwebKit.WinUI/Views/Settings/`

## Dependencies

- Related active feature: `docs/features/active/winui3-cutover-audit-hardening/`
- Downstream dependents: `docs/features/active/winui3-settings-completeness/`, `docs/features/active/winui3-aks-parity/`, `docs/features/active/winui3-pipelines-releases-parity/`, `docs/features/active/winui3-observability-parity/`
- Architecture constraints: `docs/architecture/architecture.md`, `docs/architecture/design.md`, `docs/architecture/codebase-guide.md`
- Pitfall files that apply: `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`
- Validation command: VS Code task `build-winui`

## Risks & mitigations

- Risk: the redesign becomes abstract and loses contact with real migration pressure.  
  Mitigation: use Dashboard and Settings as the only reference adopters before widening the surface.
- Risk: shared primitives overfit the first page and become hard to reuse.  
  Mitigation: extract only the patterns already repeated across MAUI and current WinUI pages.
- Risk: visual churn hides functional parity gaps.  
  Mitigation: require each primitive to remove concrete duplication or unblock a downstream feature.
- Risk: compacting headers hides context operators still need.  
  Mitigation: keep critical actions and live state visible, but push secondary guidance closer to the specific content it affects.

## Related documents

- Baseline migration archive: `docs/features/archive/winui3-migration/`
- Settings and configuration functionality: `docs/architecture/functionalities/settings-and-configuration.md`
- Cutover umbrella: `docs/features/active/winui3-cutover-audit-hardening/`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `src/SwebKit.WinUI/Controls/Shared/`, `src/SwebKit.WinUI/Views/Dashboard/`, `src/SwebKit.WinUI/Views/Settings/`, `src/SwebKit.WinUI/Controls/Shell/`
