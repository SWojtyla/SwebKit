# Feature Overview - winui3-observability-parity

---

title: "Feature Overview - winui3-observability-parity"
owner: ""
status: "In Progress"
jira: "not linked"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Bring the remaining richer Observability analysis surfaces to native WinUI parity, including deployment comparison, SLO status, and real dimension pivots, while correcting the plan to match the already-landed native logs or query baseline and reducing the remaining page seam.

## Value

Observability already has native routing, discovery, readiness handling, and a native guided or advanced logs workflow. The remaining gap is the richer overview analysis surface plus the oversized WinUI view-model seam that still owns discovery, tabs, charts, deployment context, and query state together. This feature isolates both the parity work and the seam reduction it still needs.

## Scope

### Wave 1 - Overview parity and doc correction

- Align the feature docs with the existing native logs or query baseline so the active plan matches reality.
- Add the remaining overview analysis surfaces that MAUI already exposes: deployment comparison, SLO status, and real cloud-role or operation pivots.

### Wave 2 - Page seam reduction

- Split the current Observability page responsibilities so resource discovery, provider activation, tab state, charts, and query editing do not continue to accumulate in one view-model.

### Wave 3 - Validation and hardening

- Add focused validation for overview parity, discovery, readiness transitions, and the existing native query-editor workflow.

## Out of scope

- Replacing the underlying observability providers.
- New analytics capabilities that do not already exist in MAUI.

## Source surfaces

- MAUI baseline: `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
- WinUI target: `src/SwebKit.WinUI/Views/Observability/`, `src/SwebKit.WinUI/ViewModels/Observability/`, `src/SwebKit.WinUI/Services/WorkspaceReadinessFormatter.cs`

## Dependencies

- Shared baselines available from: `docs/features/active/winui3-layout-redesign/`, `docs/features/active/winui3-settings-completeness/`
- Related active feature: `docs/features/active/winui3-cutover-audit-hardening/`
- Functionality baseline: `docs/architecture/functionalities/observability.md`
- Pitfall files that apply: `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`
- Relevant automated surface: `tests/SwebKit.WinUI.Tests/`

## Parallel execution contract

- This feature owns `src/SwebKit.WinUI/Views/Observability/`, `src/SwebKit.WinUI/ViewModels/Observability/`, and route-local readiness wiring for the Observability workspace.
- It may consume the current layout baseline and Settings deep-link contract without waiting on further global feature sequencing.
- Only missing shared Settings IA or reusable shell-contract gaps should leave this feature.

## Risks & mitigations

- Risk: further parity work keeps expanding the current oversized view-model.  
  Mitigation: treat seam reduction as explicit remaining scope instead of hiding it behind feature delivery.
- Risk: readiness handling, overview analysis, and query-state UX collide.  
  Mitigation: keep readiness, overview analysis, and logs editor state as separate responsibilities.
- Risk: the active docs drift behind the code again.  
  Mitigation: update the feature plan and functionality note in the same change set whenever the native parity surface moves.

## Related documents

- Cutover umbrella: `docs/features/active/winui3-cutover-audit-hardening/`
- Observability functionality: `docs/architecture/functionalities/observability.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: none yet
