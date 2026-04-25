# Feature Overview - winui3-service-bus-parity

---

title: "Feature Overview - winui3-service-bus-parity"
owner: ""
status: "Planned"
jira: "not linked"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Bring the remaining MAUI Service Bus operator workflows onto native WinUI so advanced message operations no longer require the Blazor host.

## Value

The WinUI Service Bus page already covers baseline browsing and core message work. The remaining parity gaps are operational depth: scheduled workflows, saved templates, advanced list control, and destructive safety cues. This feature isolates that remaining work from the cutover umbrella.

## Scope

- In scope: scheduled message manager parity, saved templates and reuse flows, advanced filters and column control, destructive bulk-operation safety, and workspace-restore hardening.
- In scope: aligning the page with the shared layout and settings primitives once those features land.
- Out of scope: new broker workflows that do not already exist in MAUI, backend Azure Service Bus redesign, and Incident Timeline integration.

## Source surfaces

- MAUI baseline: `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`, `src/SwebKit.App/Components/Pages/ServiceBusConfigForm.razor`
- WinUI target: `src/SwebKit.WinUI/Views/ServiceBus/`, `src/SwebKit.WinUI/ViewModels/ServiceBus/`

## Dependencies

- Prerequisite active features: `docs/features/active/winui3-layout-redesign/`, `docs/features/active/winui3-settings-completeness/`
- Related active feature: `docs/features/active/winui3-cutover-audit-hardening/`
- Functionality baseline: `docs/architecture/functionalities/service-bus.md`
- Pitfall files that apply: `docs/pitfalls/azure-sdk.md`, `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/blazor-maui.md`

## Risks & mitigations

- Risk: destructive flows migrate without the production-safety cues already present in MAUI.  
  Mitigation: treat bulk confirmations and safety copy as parity-critical, not polish.
- Risk: template and filter state drift from the current workspace-restore behavior.  
  Mitigation: validate save, restore, and reload paths explicitly.

## Related documents

- Cutover umbrella: `docs/features/active/winui3-cutover-audit-hardening/`
- Service Bus functionality: `docs/architecture/functionalities/service-bus.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: none yet
