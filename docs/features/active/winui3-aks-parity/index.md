# Feature Overview - winui3-aks-parity

---

title: "Feature Overview - winui3-aks-parity"
owner: ""
status: "In Progress"
jira: "not linked"
created: "2026-04-25"
updated: "2026-04-26"

---

## Goal

Close the remaining AKS parity gap in the native WinUI workspace so operators can inspect and act on clusters without losing the richer diagnostics and resource coverage still present in MAUI.

## Value

The WinUI AKS route already proves native browsing, logs, port-forwarding, and shell launch. The current slice closes broader resource coverage, current diagnostic and operational language, and the content-first native page layout needed to keep the explorer and detail workspace as the main visual focus, while leaving deeper MAUI-only evidence panels and live-cluster validation as explicit follow-up.

## Scope

- In scope: broader resource coverage where MAUI still exposes more surfaces, richer diagnostics panels, and deeper operational actions that belong to the existing AKS workflow.
- In scope: page-local adoption of the current shared layout primitives for diagnostic cards and detail panes.
- Out of scope: AKS backend redesign, new cluster-management features, or new infrastructure provisioning flows.

## Source surfaces

- MAUI baseline: `src/SwebKit.App/Components/Pages/AksPage.razor`, `src/SwebKit.App/Components/Pages/AksConfigForm.razor`
- WinUI target: `src/SwebKit.WinUI/Views/Aks/`, `src/SwebKit.WinUI/ViewModels/Aks/`

## Dependencies

- Shared baselines available from: `docs/features/active/winui3-layout-redesign/`, `docs/features/active/winui3-settings-completeness/`
- Related active feature: `docs/features/active/winui3-cutover-audit-hardening/`
- Functionality baseline: `docs/architecture/functionalities/aks.md`
- Pitfall files that apply: `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`

## Parallel execution contract

- This feature owns `src/SwebKit.WinUI/Views/Aks/`, `src/SwebKit.WinUI/ViewModels/Aks/`, and AKS-specific diagnostics and action flows.
- It may consume the current shared layout primitives and Settings repair path without waiting on more global plan work.
- Reusable cross-page primitive gaps should be raised separately; AKS page-local adoption stays inside this feature.

## Risks & mitigations

- Risk: diagnostics panels become another page-local layout island.  
  Mitigation: treat AKS as a primary adopter of the shared metric and detail primitives.
- Risk: action parity lands without safe cancellation or disposal behavior.  
  Mitigation: extend the existing deferred-load and disposal hardening into the new AKS flows.

## Related documents

- Cutover umbrella: `docs/features/active/winui3-cutover-audit-hardening/`
- AKS functionality: `docs/architecture/functionalities/aks.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: none yet
