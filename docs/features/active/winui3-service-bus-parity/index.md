# Feature Overview - winui3-service-bus-parity

---

title: "Feature Overview - winui3-service-bus-parity"
owner: ""
status: "In Progress"
jira: "not linked"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Bring the highest-value remaining MAUI Service Bus operator workflows onto native WinUI so scheduled work, compose/template reuse, and core destructive message operations no longer require the Blazor host, while keeping the remaining advanced-rule/custom-column and restore-hardening gaps explicit.

## Value

The WinUI Service Bus page now covers the main native parity baseline: scheduled-message management, compose/template reuse with scheduled send, richer message-list controls, and confirmation-gated destructive actions. Deferred follow-up still tracked under this feature includes the hosted-only advanced filter/export/purge/row-density/custom-column/full-template-management workflows plus workspace-restore hardening and page-level WinUI coverage.

## Scope

- In scope for the landed native baseline: scheduled message manager tabs, template save/apply/reuse flows, compose send-or-schedule parity, text-filter and list-preference persistence, and destructive safety cues.
- Deferred follow-up still tracked here: hosted-only filter/export/purge/row-density/custom-column/template-management parity, workspace-restore hardening, and page-level WinUI coverage.
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
