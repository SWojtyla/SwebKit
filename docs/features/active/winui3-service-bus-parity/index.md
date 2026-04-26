# Feature Overview - winui3-service-bus-parity

---

title: "Feature Overview - winui3-service-bus-parity"
owner: ""
status: "Review"
jira: "not linked"
created: "2026-04-25"
updated: "2026-04-26"

---

## Goal

Finish migrating the remaining non-incident MAUI Service Bus operator workflows onto native WinUI so replay, batch DLQ replay, richer list tooling, and workspace restore behavior no longer require the Blazor host.

## Value

The WinUI Service Bus page now covers the earlier native baseline plus the remaining non-incident parity lift: replay target selection, batch DLQ replay, advanced list tooling, and workspace favorite or restore behavior are available natively. Incident investigation and trace pivots are explicitly excluded from this feature because that surface is being removed rather than migrated.

## Scope

- In scope: complete the remaining non-incident MAUI-owned Service Bus workflows, including batch send, selected-message quick actions, replay parity, richer list controls, batch DLQ replay, and workspace restore or favorite behavior.
- In scope for the work completed in this pass: replay target selection, batch DLQ replay, multi-rule list filtering, filtered delete, purge, export JSON, row density, custom application-property columns, and shell workspace snapshot parity.
- In scope: page-local adoption of the current shared layout primitives and Settings repair contract where Service Bus needs them.
- Out of scope: incident investigation or trace-pivot parity, new broker workflows that do not already exist in MAUI, and backend Azure Service Bus redesign.

## Source surfaces

- MAUI baseline: `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`, `src/SwebKit.App/Components/Pages/ServiceBusConfigForm.razor`
- WinUI target: `src/SwebKit.WinUI/Views/ServiceBus/`, `src/SwebKit.WinUI/ViewModels/ServiceBus/`

## Dependencies

- Shared baselines available from: `docs/features/active/winui3-layout-redesign/`, `docs/features/active/winui3-settings-completeness/`
- Related active feature: `docs/features/active/winui3-cutover-audit-hardening/`
- Functionality baseline: `docs/architecture/functionalities/service-bus.md`
- Pitfall files that apply: `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`

## Parallel execution contract

- This feature owns `src/SwebKit.WinUI/Views/ServiceBus/`, `src/SwebKit.WinUI/ViewModels/ServiceBus/`, and Service Bus-specific workspace state.
- It may consume the current layout primitives and Settings section contract without waiting on further global redesign work.
- Reusable primitive gaps or global Settings IA changes should be raised separately; page-local adoption stays here.

## Risks & mitigations

- Risk: destructive flows migrate without the production-safety cues already present in MAUI.  
  Mitigation: treat bulk confirmations and safety copy as parity-critical, not polish.
- Risk: template and filter state drift from the current workspace-restore behavior.  
  Mitigation: validate save, restore, and reload paths explicitly.
- Risk: repo-wide WinUI validation can be blocked by unrelated feature work in the same project.  
  Mitigation: keep focused Service Bus page tests green and call out external build blockers explicitly in `status.md` and `test-plan.md`.

## Related documents

- Cutover umbrella: `docs/features/active/winui3-cutover-audit-hardening/`
- Service Bus functionality: `docs/architecture/functionalities/service-bus.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: none yet
