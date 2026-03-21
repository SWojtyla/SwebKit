# Archive Summary — Global Notification System

---

title: "Archive Summary - Global Notification System"
owner: ""
completed_date: "2026-03-21"
pr: ""
commit: ""

---

## Goal

Introduce a global, reusable toast notification system giving users consistent, non-blocking feedback after completing actions (success, warning, error, info) across all feature areas, replacing silent failures and absent success confirmations.

## Delivered

- `NotificationModels.cs` — `Notification` record + `NotificationSeverity` enum (`SwebKit.Core/Models/`)
- `INotificationService` interface (`SwebKit.Core/Abstractions/`) — `ShowSuccess`, `ShowInfo`, `ShowWarning`, `ShowError`, `Dismiss`, `ClearAll`, `All` snapshot, `NotificationsChanged` event
- `NotificationService` — thread-safe singleton implementation with `lock` on list, immutable snapshot on `All`, exception message appended to detail (`SwebKit.App/Services/`)
- `NotificationToast.razor` — fixed top-right overlay, up to 4 stacked toasts, per-notification auto-dismiss timers (4s/8s), slide-in CSS animation, close button
- `NotificationHistory.razor` — full session history panel, reverse-chronological, clear-all, empty-state
- `TopBar.razor` — bell icon with unread badge, toggles history panel
- `MainLayout.razor` — hosts `<NotificationToast>` at app level
- DI registration as singleton in `MauiProgram.cs`
- Integration across Service Bus, AKS, Redis, Storage, and Releases pages
- 17 unit tests in `NotificationServiceTests.cs` covering severity, event firing, dismiss, clear, concurrency, snapshot isolation, and unique IDs
- Fixed pre-existing test project build blockers: missing `Microsoft.FluentUI.AspNetCore.Components.Icons` package, missing `Icons` alias in `_Imports.razor`, stale `NavItem.Icon` parameter reference in `ComponentTests.cs`

## Key decisions

- **Service resides in `SwebKit.App`, interface in `SwebKit.Core`** — the implementation uses `lock` (not `Lock`/`Mutex`) to stay compatible with net10.0 and keep DI simple. Interface in Core keeps feature pages decoupled from the App project.
- **No auto-expiry in the service** — expiry timers live in the UI component (`NotificationToast.razor`) only, keeping the service pure and easily testable.
- **`All` returns an immutable snapshot** — `_notifications.ToList()` under lock prevents external callers from mutating the internal list; also ensures test isolation.
- **`Dismiss(Guid id)` not `string`** — used `Guid` for the notification ID to match the `Notification` record definition, avoiding string parsing at the service layer.

## Validation performed

- 17 unit tests covering all `NotificationService` public methods, including concurrent access (100 parallel `ShowSuccess` calls)
- All 59 tests in `SwebKit.App.Tests` pass (0 failures)
- Build: 0 errors, 1 pre-existing `RZ10012` warning for `NotificationHistory` (unregistered component in test project; runtime-only component, not linked)

## Lessons learned

- **Test project needs both FluentUI packages.** The main app references `Microsoft.FluentUI.AspNetCore.Components.Icons` separately from the base package. When linking app components into the test project, the icons package must also be referenced or `Icons` will not resolve.
- **Razor `_Imports.razor` aliases don't carry into `.cs` files.** A `@using Icons = ...` alias in `_Imports.razor` works for Razor component files but is invisible to plain C# test files — add the alias as a regular C# `using` directive where needed.
- **New `[Inject]` properties on existing components break existing bUnit tests.** Any test that renders a component now injecting `INotificationService` must register it in `Services` or the render throws `InvalidOperationException`. Review all component tests when adding a new injected dependency.

## Follow-up

- Incremental migration of inline `ErrorCallout` usages to `ShowError` — no hard deadline, can be done per-feature as those pages are next touched.
- Manual E2E verification of toast animation, auto-dismiss timing, and bell badge — deferred to next time the app is run on a physical Windows device.

## Archive metadata

- Feature folder: `docs/features/active/notification-system/`
- Related source: `src/SwebKit.Core/Models/NotificationModels.cs`, `src/SwebKit.Core/Abstractions/INotificationService.cs`, `src/SwebKit.App/Services/NotificationService.cs`, `src/SwebKit.App/Components/Notifications/`
- Tests: `tests/SwebKit.App.Tests/NotificationServiceTests.cs`
