# Feature Overview - winui3-observability-parity

---

title: "Feature Overview - winui3-observability-parity"
owner: ""
status: "Planned"
jira: "not linked"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Bring Observability to native WinUI parity, including the missing query-editor and richer analysis flows, while reducing the current page seam between discovery, tabs, charts, and query state.

## Value

Observability already has native routing, discovery, several analysis tabs, and readiness handling. The remaining gap is deeper than missing visuals: MAUI still owns the richer KQL editing and analysis workflow, and the current WinUI page seam is too broad to absorb that safely. This feature isolates both the parity work and the seam reduction it needs.

## Scope

### Wave 1 - Page seam reduction

- Split the current Observability page responsibilities so resource discovery, provider activation, tab state, and query editing do not continue to accumulate in one view-model.

### Wave 2 - Workflow parity

- Add the missing query-editor path and the broader chart and drill-through flows that still remain narrower than MAUI.
- Keep the readiness-state behavior added during hardening, but align it with the completed native Settings repair surface.

### Wave 3 - Validation and hardening

- Add focused validation for discovery, tab state, readiness transitions, and the new query-editor workflow.

## Out of scope

- Replacing the underlying observability providers.
- New analytics capabilities that do not already exist in MAUI.

## Source surfaces

- MAUI baseline: `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
- WinUI target: `src/SwebKit.WinUI/Views/Observability/`, `src/SwebKit.WinUI/ViewModels/Observability/`, `src/SwebKit.WinUI/Services/WorkspaceReadinessFormatter.cs`

## Dependencies

- Prerequisite active features: `docs/features/active/winui3-layout-redesign/`, `docs/features/active/winui3-settings-completeness/`
- Related active feature: `docs/features/active/winui3-cutover-audit-hardening/`
- Functionality baseline: `docs/architecture/functionalities/observability.md`
- Pitfall files that apply: `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`
- Relevant automated surface: `tests/SwebKit.WinUI.Tests/`

## Risks & mitigations

- Risk: the missing query-editor path gets bolted into the current oversized view-model.  
  Mitigation: reduce the page seam before adding editor behavior.
- Risk: readiness handling and query-state UX collide.  
  Mitigation: keep readiness, discovery, and editor state as separate responsibilities.
- Risk: the editor host is treated as a generic platform project and delays the feature indefinitely.  
  Mitigation: scope the editor host to the concrete Observability workflow first, then generalize only if another feature actually needs it.

## Related documents

- Cutover umbrella: `docs/features/active/winui3-cutover-audit-hardening/`
- Observability functionality: `docs/architecture/functionalities/observability.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: none yet
