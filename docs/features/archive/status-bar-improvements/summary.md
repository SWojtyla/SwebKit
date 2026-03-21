# Archive Summary — Status Bar Improvements

---

title: "Archive Summary - Status Bar Improvements"
owner: ""
completed_date: "2026-03-21"
pr: ""
commit: ""

---

## Goal

Expand the status bar from a near-empty footer into a persistent context strip showing per-service connectivity, last refresh time, active port-forward sessions, and a named task indicator.

## Delivered

- `IConnectionStateService` interface (`SwebKit.Core/Abstractions/`) — `SetConnected`, `SetError`, `SetNotConfigured`, `States` dictionary, `StatesChanged` event
- `ConnectionStateService` singleton implementation (`SwebKit.Core/Services/`)
- `ConnectionStateChangedEvent` added to `AppEventBus.cs`
- `IConnectionStateService` registered in `MauiProgram.cs`
- `StatusBar.razor` rewritten: connection dots (SB / AKS / Redis / Storage / Releases), last-refresh timestamp with 10-second `PeriodicTimer` tick, named task indicator (shows title for single task, count for multiple), port-forward count retained
- `SetConnected`/`SetError`/`SetNotConfigured` wired into `ServiceBusPage`, `AksPage`, `RedisPage`, `StoragePage`, `ReleasesPage` at init / connection time
- `ProjectEnvironment` renamed to `AppConfig` across the full codebase (class, file, all razor and C# references); active architecture docs (`architecture.md`, `design.md`, `settings-and-configuration.md`) updated to reflect that no project/environment selection model exists

## Key decisions

- **Environment name / prod badge deferred** — `AppConfig` has no environment name or production tier field. Adding them belongs in a settings overhaul, not this feature. See `decisions.md` D-1.
- **Connection state reported at page init, not per-operation** — wiring every individual client call would be invasive. The dot reflects "did this area connect when last opened?" which is the meaningful signal. See `decisions.md` D-2.
- **`ConnState` as the inject alias in `StatusBar.razor`** — `ConnectionState` would collide with the `ConnectionState` enum type in the same Razor scope. Alias avoids the ambiguity without qualifying every enum value.

## Validation performed

- Manual: app confirmed to run and build with 0 errors after all changes (app was live during second build — lock errors only, no compile errors).
- Connection dots, refresh timestamp, and task name require manual walkthrough when running.

## Follow-up

- Environment name / prod badge: implement once `AppConfig` gains a name/tier concept.
- Connection dots only appear after a page is first visited — a future improvement could pre-populate state on app start.

## Archive metadata

- Feature folder: `docs/features/active/status-bar-improvements/`
- Related source: `src/SwebKit.Core/Abstractions/IConnectionStateService.cs`, `src/SwebKit.Core/Services/ConnectionStateService.cs`, `src/SwebKit.App/Components/Layout/StatusBar.razor`, `src/SwebKit.Core/Domain/AppConfig.cs`
