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

Close the remaining AKS parity gap in the native WinUI workspace so operators can inspect and act on clusters without losing the richer diagnostics, discoverability, and workflow continuity still present in MAUI.

## Value

The WinUI AKS route already proves that native browsing, logs, port-forwarding, and shell launch are viable. The remaining work is no longer about proving feasibility; it is about removing the operator regressions that still make the page feel worse than MAUI in day-to-day use. This slice focuses on restoring saved scope startup, searchability, log ergonomics, row-level actions, and the compact page structure that keeps the explorer and diagnostics workspace in view without wasted chrome.

## Scope

- In scope: eliminate the known WinUI AKS regressions versus MAUI in startup behavior, namespace selection, warning noise, log ergonomics, and row-level action discovery.
- In scope: complete the remaining WinUI AKS action parity that belongs to the existing page and view-model surface.
- In scope: keep the page compact and content-first so the explorer remains the primary workspace at standard desktop sizes.
- Out of scope: AKS backend redesign, new cluster-management features, new infrastructure flows, or unrelated shell-wide layout refactors.

## Source surfaces

- MAUI baseline: `src/SwebKit.App/Components/Pages/AksPage.razor`, `src/SwebKit.App/Components/Pages/AksConfigForm.razor`
- WinUI target: `src/SwebKit.WinUI/Views/Aks/`, `src/SwebKit.WinUI/ViewModels/Aks/`
- Shared shell primitives: `src/SwebKit.WinUI/Controls/Shared/PageScaffold.xaml`, `src/SwebKit.WinUI/Controls/Shared/DetailPaneHost.xaml`, `src/SwebKit.WinUI/Controls/Shared/SectionCard.xaml`

## Dependencies

- Shared baselines available from: `docs/features/active/winui3-layout-redesign/`, `docs/features/active/winui3-settings-completeness/`
- Related active feature: `docs/features/active/winui3-cutover-audit-hardening/`
- Functionality baseline: `docs/architecture/functionalities/aks.md`
- Pitfall files that apply: `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`

## Known gaps to close

- Startup scope restore: the WinUI AKS page must honor the saved context and namespace from Settings on first load.
- Namespace selection parity: namespace switching must stay searchable and fast for large clusters.
- Warning noise: partial resource-load failures should not dominate the page when usable explorer data already loaded.
- Log ergonomics: workload and pod logs must feel like a real investigation workspace instead of a cramped half-page strip.
- Action discoverability: operators need MAUI-style right-click and row-scoped actions without hunting through the side pane.
- Layout discipline: no tall header chrome, no unnecessary vertical scroll, no oversized inactive panels.

## Planned execution slices

- Slice 1: restore startup and selector behavior parity in `AksPageViewModel` and the AKS toolbar.
- Slice 2: finish the compact page layout and warning behavior so the explorer stays dominant.
- Slice 3: complete logs and row-action parity, including the remaining MAUI secondary actions that belong in WinUI.
- Slice 4: validate the resulting native flow in both focused tests and live-cluster manual checks.

## Parallel execution contract

- This feature owns `src/SwebKit.WinUI/Views/Aks/`, `src/SwebKit.WinUI/ViewModels/Aks/`, and AKS-specific diagnostics and action flows.
- It may consume the current shared layout primitives and Settings repair path without waiting on more global plan work.
- Reusable cross-page primitive gaps should be raised separately; AKS page-local adoption stays inside this feature.

## Risks & mitigations

- Risk: diagnostics panels become another page-local layout island.  
  Mitigation: treat AKS as a primary adopter of the shared metric and detail primitives.
- Risk: action parity lands without safe cancellation or disposal behavior.  
  Mitigation: extend the existing deferred-load and disposal hardening into the new AKS flows.
- Risk: parity work turns into another round of one-off UI tweaks without a durable slice boundary.  
  Mitigation: keep the remaining work grouped by operator workflow seams: startup scope, selector/search, logs, row actions, and validation.
- Risk: the page appears "done" because code exists, while major operator regressions remain.  
  Mitigation: keep this feature in `In Progress` until the known parity complaints are closed and revalidated.

## Related documents

- Cutover umbrella: `docs/features/active/winui3-cutover-audit-hardening/`
- AKS functionality: `docs/architecture/functionalities/aks.md`
- Implementation module: `frontend.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `frontend.md`
