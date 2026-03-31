# Feature Overview - windows-tray-aks-monitoring

---

title: "Feature Overview - windows-tray-aks-monitoring"
owner: "GitHub Copilot"
status: "Planned"
jira: "not linked"
created: "2026-03-31"
updated: "2026-03-31"

---

## Goal

Allow the app to minimize to the Windows system tray (from both Minimize and Close actions) while continuing AKS pod health monitoring for the namespaces configured in the app.

## Value

AKS namespace monitoring is designed to run continuously. Today, users can close the main window and lose active monitoring visibility. Tray behavior keeps monitoring active in the background, reduces desktop clutter, and still surfaces actionable pod alerts through toast notifications plus tray state.

## Scope

- In scope:
- Wave 1 - Add Windows tray lifecycle handling for hide, restore, and explicit exit.
- Wave 2 - Keep existing AKS monitor loop running while window is hidden.
- Wave 3 - Add tray UX (icon, tooltip state, unread alert indicator, restore/exit actions).
- Wave 4 - Add automated + manual validation and align functionality documentation.
- Out of scope:
- Build/package-size optimization work (request clarified as minimize-to-tray behavior only).
- Cross-platform tray behavior (Windows only).
- New AKS signal sources or monitoring semantics beyond configured namespaces.
- Reworking existing toast payload format for pod alerts.

## Dependencies

- Runtime and composition paths:
- src/SwebKit.App/App.xaml.cs
- src/SwebKit.App/MauiProgram.cs
- src/SwebKit.App/Services/PodHealthMonitorService.cs
- src/SwebKit.App/Platforms/Windows/WindowsToastNotificationService.cs
- src/SwebKit.App/Components/Pages/AksPage.razor
- src/SwebKit.Core/Domain/AksConfig.cs
- src/SwebKit.Core/Configuration/UiStateRepository.cs
- Architecture and functionality docs expected to be updated during implementation:
- docs/architecture/functionalities/aks.md
- docs/architecture/functionalities/settings-and-configuration.md
- Pitfalls that apply:
- docs/pitfalls/blazor-maui.md
- docs/pitfalls/dotnet-csharp.md
- docs/pitfalls/agent-workflow.md

## Risks & mitigations

- Risk: Close-to-tray behavior could conflict with real application shutdown.
- Mitigation: Introduce explicit "Exit" flow that bypasses hide interception and preserves existing process-exit cleanup.
- Risk: Tray indicator updates may cross UI thread boundaries from background monitor callbacks.
- Mitigation: Centralize window/tray updates behind dispatcher-safe service methods.
- Risk: Hidden-window mode may be interpreted as app exit by users.
- Mitigation: Add first-run confirmation toast/message and clear tray menu actions.
- Risk: Duplicate tray icons or stale handles on repeated hide/restore cycles.
- Mitigation: Single-instance tray icon lifecycle with deterministic disposal.

## Related documents

- Architecture map: docs/architecture/architecture.md
- Component design: docs/architecture/design.md
- Code navigation: docs/architecture/codebase-guide.md
- AKS functionality deep dive: docs/architecture/functionalities/aks.md

## Quick links

- Jira: not linked
- Status: status.md
- Tests: test-plan.md
- Implementation modules: backend.md, frontend.md, decisions.md
