# Feature Overview - winui3-pipelines-releases-parity

---

title: "Feature Overview - winui3-pipelines-releases-parity"
owner: ""
status: "Planned"
jira: "not linked"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Bring Pipelines and Releases to native WinUI parity while reducing the current page and view-model pressure that has accumulated around project scope, approvals, release history, and release-tag management.

## Value

Pipelines is already one of the hardest WinUI routes: it owns environment-sensitive readiness handling, multiple nested workspaces, and an increasingly broad view-model. The MAUI comparison shows that parity is not just missing screens, but also missing structure. This feature isolates both the workflow parity and the refactor required to keep the route maintainable.

## Scope

### Wave 1 - Page seam reduction

- Split the current Pipelines page and view-model responsibilities into more focused native surfaces where project selection, approvals, release history, and release-tag workflows no longer compete inside one class.

### Wave 2 - Workflow parity

- Restore the deeper tree/detail, validation, and editing workflows that still remain narrower than MAUI.
- Keep the readiness-state behavior added during hardening, but align it with the completed native Settings repair surface.

### Wave 3 - Validation and hardening

- Add focused validation for the refactored page seams and the Azure DevOps readiness or failure paths.

## Out of scope

- Replacing the underlying Azure DevOps domain services.
- New Azure DevOps workflows that do not already exist in MAUI.

## Source surfaces

- MAUI baseline: `src/SwebKit.App/Components/Pages/PipelinesPage.razor`, `src/SwebKit.App/Components/Pages/DevOpsConfigForm.razor`
- WinUI target: `src/SwebKit.WinUI/Views/Pipelines/`, `src/SwebKit.WinUI/ViewModels/Pipelines/`, `src/SwebKit.WinUI/Services/WorkspaceReadinessFormatter.cs`

## Dependencies

- Prerequisite active features: `docs/features/active/winui3-layout-redesign/`, `docs/features/active/winui3-settings-completeness/`
- Related active feature: `docs/features/active/winui3-cutover-audit-hardening/`
- Functionality baseline: `docs/architecture/functionalities/releases.md`
- Pitfall files that apply: `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`
- Relevant automated surface: `tests/SwebKit.DevOps.Tests/`, `tests/SwebKit.WinUI.Tests/`

## Risks & mitigations

- Risk: parity work keeps widening the current oversized `PipelinesPageViewModel`.  
  Mitigation: reduce the page seam before restoring more workflow depth.
- Risk: readiness behavior and Settings repair paths drift apart.  
  Mitigation: treat readiness-to-settings navigation as part of the feature scope.
- Risk: live-environment validation becomes impossible to repeat.  
  Mitigation: keep both demo-mode and live Azure DevOps checks explicit in the test plan.

## Related documents

- Cutover umbrella: `docs/features/active/winui3-cutover-audit-hardening/`
- Releases functionality: `docs/architecture/functionalities/releases.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: none yet
