# Feature Overview - winui3-pipelines-releases-parity

---

title: "Feature Overview - winui3-pipelines-releases-parity"
owner: ""
status: "Done"
jira: "not linked"
created: "2026-04-25"
updated: "2026-04-26"

---

## Goal

Capture the first native Pipelines and Releases seam-reduction and readiness slice in WinUI while keeping deeper workflow restoration explicit as future follow-up instead of leaving the coordination surface open indefinitely.

## Value

Pipelines is already one of the hardest WinUI routes: it owns environment-sensitive readiness handling, multiple nested workspaces, and an increasingly broad view-model. The delivered slice closes the first native seam reduction and readiness contract work, while leaving deeper tree or detail, approval, editing, and live Azure DevOps validation explicit as future follow-up.

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

- Shared baselines available from: `docs/features/active/winui3-layout-redesign/`, `docs/features/active/winui3-settings-completeness/`
- Related active feature: `docs/features/active/winui3-cutover-audit-hardening/`
- Functionality baseline: `docs/architecture/functionalities/releases.md`
- Pitfall files that apply: `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`
- Relevant automated surface: `tests/SwebKit.DevOps.Tests/`, `tests/SwebKit.WinUI.Tests/`

## Parallel execution contract

- This feature owns `src/SwebKit.WinUI/Views/Pipelines/`, `src/SwebKit.WinUI/ViewModels/Pipelines/`, and route-local readiness wiring for the Pipelines workspace.
- It may consume the current layout baseline and Settings deep-link contract without waiting on further global feature sequencing.
- Only missing shared Settings IA or reusable shell-contract gaps should leave this feature.

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
