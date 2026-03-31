# Backend Plan - windows-tray-aks-monitoring

---

title: "Backend Plan - windows-tray-aks-monitoring"
owner: "GitHub Copilot"
status: "Done"

---

## Goal

Add a Windows tray/window lifecycle backend that hides the app instead of closing it on Minimize/Close actions, while keeping existing `IPodHealthMonitorService` execution and AKS namespace configuration behavior unchanged.

## Impacted areas

- App lifecycle and DI:
- src/SwebKit.App/App.xaml.cs
- src/SwebKit.App/MauiProgram.cs
- Monitoring and event source:
- src/SwebKit.App/Services/PodHealthMonitorService.cs
- src/SwebKit.Core/Abstractions/IPodHealthMonitorService.cs
- src/SwebKit.Core/Domain/AksConfig.cs
- Windows platform implementation:
- src/SwebKit.App/Platforms/Windows/
- Potential additions for clear separation:
- src/SwebKit.App/Services/ITrayWindowService.cs
- src/SwebKit.App/Platforms/Windows/WindowsTrayWindowService.cs

## Design

Introduce a Windows-specific tray/window coordinator service that owns:

- Window hide/restore transitions.
- Close interception with explicit-exit bypass.
- Single tray icon/menu lifecycle.
- Unread alert indicator state sourced from pod health events.

Data flow (target):

1. App startup initializes tray coordinator with the main window reference in `App.CreateWindow`.
2. Window Minimize or Close triggers coordinator interception.
3. Coordinator hides window and keeps process alive unless explicit Exit was requested from tray menu.
4. `PodHealthMonitorService.PodHealthDetected` events update tray indicator state while hidden.
5. Tray Restore shows/focuses main window and resets indicator according to UX rule.
6. Tray Exit sets explicit-exit flag and allows normal process termination, preserving existing `OnProcessExit` cleanup.

This keeps monitoring logic in the current monitor service and adds no second background worker.

## API / Contracts

- Prefer app-layer contracts over direct page-level platform calls so behavior is testable.
- Proposed app-layer contract surface:
- `Initialize(Window window)`
- `HideToTray()`
- `RestoreFromTray()`
- `RequestExit()`
- `SetAlertIndicator(int unreadCount)`
- If cross-layer consumption is needed, expose minimal abstraction and keep platform-specific details in `Platforms/Windows`.

## Tasks

- [x] Define tray/window coordinator interface and lifecycle responsibilities (`ITrayLifecycleService`).
- [x] Implement Windows tray service with deterministic icon/menu disposal (`WindowsTrayLifecycleService`).
- [x] Wire startup initialization in `App.xaml.cs` and DI registration in `MauiProgram.cs`.
- [x] Intercept Minimize and Close to hide to tray by default.
- [x] Add explicit exit path from tray that preserves current process-exit cleanup.
- [x] Connect pod health events to tray indicator updates.
- [x] Add logging around hide/restore/exit transitions and tray failures.
- [x] Add/update unit tests for coordinator logic and monitor continuity assumptions (`TrayLifecycleStateTests` — 4/4 pass).
- [x] Record design decisions in `decisions.md`.

## Migration and runtime changes

- No external infrastructure migration.
- Runtime behavior change: Close action no longer exits app directly; explicit Exit path is introduced in tray menu.
- Existing persisted AKS monitor fields (`MonitoringEnabled`, `MonitoredNamespaces`) remain source of truth.

## Validation

- Unit tests: Not started
- Integration tests: Not started
- Manual checks:
- Validate close/minimize interception does not terminate process.
- Validate explicit tray exit terminates process and runs cleanup hooks.
- Validate monitoring continues and alert indicator updates while hidden.

## Notes

- Keep `AppDomain.CurrentDomain.ProcessExit` cleanup path intact for port-forward session shutdown.
- Avoid coupling tray logic to `AksPage` component lifecycle; monitoring must continue even when AKS page is not active.
