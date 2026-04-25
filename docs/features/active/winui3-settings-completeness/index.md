# Feature Overview - winui3-settings-completeness

---

title: "Feature Overview - winui3-settings-completeness"
owner: ""
status: "Planned"
jira: "not linked"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Restore full native settings coverage for the in-scope WinUI operator domains so configuration, readiness guidance, and credential repair no longer depend on the MAUI host.

## Value

The current WinUI Settings page only exposes Appearance and General. That leaves AKS, Redis, Storage, Pipelines/Releases, Observability, and Service Bus dependent on missing or hidden configuration surfaces even where route shells already exist. This feature closes that gap and becomes the operator-controlled entry point for fixing readiness issues.

## Scope

### Wave 1 - Section parity

- Add native settings sections for Service Bus, AKS, Redis, DevOps, Storage, and Observability.
- Bring over the MAUI config-form intent from `*ConfigForm.razor` without reintroducing the Blazor page structure.
- Keep Incident Timeline explicitly out of scope, but make the omission visible instead of silent if the settings IA would otherwise imply parity.

### Wave 2 - Validation and readiness

- Add per-section validation and operator guidance that downstream routes can deep-link into.
- Align error and readiness copy with the route-level readiness states already implemented in Pipelines and Observability.

### Wave 3 - Persistence and handoff

- Verify that settings persistence, credential references, and navigation handoff match the current host services.
- Make the Settings page the documented repair path for environment-sensitive failures.

## Out of scope

- Implementing the domain workspaces themselves.
- Migrating Incident Timeline beyond explicitly deferring it.
- Replacing credential storage or backend configuration services unless a concrete gap is found.

## Source surfaces

- MAUI settings baseline: `src/SwebKit.App/Components/Pages/SettingsPage.razor`, `src/SwebKit.App/Components/Pages/ServiceBusConfigForm.razor`, `src/SwebKit.App/Components/Pages/AksConfigForm.razor`, `src/SwebKit.App/Components/Pages/RedisConfigForm.razor`, `src/SwebKit.App/Components/Pages/DevOpsConfigForm.razor`, `src/SwebKit.App/Components/Pages/StorageConfigForm.razor`
- WinUI target: `src/SwebKit.WinUI/Views/Settings/`, `src/SwebKit.WinUI/ViewModels/Settings/`, `src/SwebKit.WinUI/Services/WorkspaceReadinessFormatter.cs`

## Dependencies

- Prerequisite active feature: `docs/features/active/winui3-layout-redesign/`
- Related active feature: `docs/features/active/winui3-cutover-audit-hardening/`
- Downstream dependents: `docs/features/active/winui3-service-bus-parity/`, `docs/features/active/winui3-aks-parity/`, `docs/features/active/winui3-redis-parity/`, `docs/features/active/winui3-storage-parity/`, `docs/features/active/winui3-pipelines-releases-parity/`, `docs/features/active/winui3-observability-parity/`
- Functionality baseline: `docs/architecture/functionalities/settings-and-configuration.md`
- Pitfall files that apply: `docs/pitfalls/blazor-maui.md`, `docs/pitfalls/dotnet-csharp.md`

## Risks & mitigations

- Risk: settings parity becomes a dumping ground for domain logic.  
  Mitigation: keep Settings responsible for configuration, validation, and repair guidance only.
- Risk: route-level readiness states and Settings drift apart.  
  Mitigation: make each readiness state open the exact native settings section that owns the fix.
- Risk: hidden Incident Timeline omissions confuse operators.  
  Mitigation: record that feature as deferred explicitly in the settings IA.

## Related documents

- Baseline migration archive: `docs/features/archive/winui3-migration/`
- Cutover umbrella: `docs/features/active/winui3-cutover-audit-hardening/`
- Settings functionality: `docs/architecture/functionalities/settings-and-configuration.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: none yet
