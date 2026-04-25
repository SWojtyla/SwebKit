# Feature Overview - winui3-aks-parity

---

title: "Feature Overview - winui3-aks-parity"
owner: ""
status: "Planned"
jira: "not linked"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Close the remaining AKS parity gap in the native WinUI workspace so operators can inspect and act on clusters without losing the richer diagnostics and resource coverage still present in MAUI.

## Value

The WinUI AKS route already proves native browsing, logs, port-forwarding, and shell launch. The remaining parity work is broader resource coverage plus the diagnostic-card language that MAUI accumulated for health, events, and operational insight. This feature isolates that higher-pressure refactor work.

## Scope

- In scope: broader resource coverage where MAUI still exposes more surfaces, richer diagnostics panels, and deeper operational actions that belong to the existing AKS workflow.
- In scope: adopting the shared layout primitives for diagnostic cards and detail panes.
- Out of scope: AKS backend redesign, new cluster-management features, or new infrastructure provisioning flows.

## Source surfaces

- MAUI baseline: `src/SwebKit.App/Components/Pages/AksPage.razor`, `src/SwebKit.App/Components/Pages/AksConfigForm.razor`
- WinUI target: `src/SwebKit.WinUI/Views/Aks/`, `src/SwebKit.WinUI/ViewModels/Aks/`

## Dependencies

- Prerequisite active features: `docs/features/active/winui3-layout-redesign/`, `docs/features/active/winui3-settings-completeness/`
- Related active feature: `docs/features/active/winui3-cutover-audit-hardening/`
- Functionality baseline: `docs/architecture/functionalities/aks.md`
- Pitfall files that apply: `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`

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
