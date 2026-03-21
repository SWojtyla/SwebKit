# Status — Global Notification System

---

title: "Status - Global Notification System"
owner: ""
state: "In Progress"
branch: ""
started: "2026-03-21"
last_updated: "2026-03-21"

---

## Quick summary

Current state: In Progress — core service and UI components implemented. Integration across feature pages remaining.

## Progress checklist

- [x] Planning complete
- [x] Design reviewed
- [x] Backend implementation (`INotificationService`)
- [x] Frontend implementation (`NotificationToast.razor`, `NotificationHistory.razor`)
- [x] Integration across all feature pages
- [ ] Tests (unit / manual)
- [ ] Docs aligned
- [ ] Ready for review

## Completed

- Feature scoped in `index.md`
- `NotificationModels.cs` — `Notification` record + `NotificationSeverity` enum (in `SwebKit.Core/Models/`)
- `INotificationService.cs` — interface in `SwebKit.Core/Abstractions/`
- `NotificationService.cs` — thread-safe singleton implementation in `SwebKit.App/Services/`
- DI registration in `MauiProgram.cs` (`AddSingleton<INotificationService, NotificationService>`)
- `NotificationToast.razor` — fixed top-right overlay, up to 4 stacked toasts, per-notification auto-dismiss timers (4s/8s), slide-in CSS animation, close button
- `NotificationHistory.razor` — full session history panel, reverse chronological, clear-all button, empty state
- `TopBar.razor` — bell icon with unread badge, toggles history panel
- `MainLayout.razor` — hosts `<NotificationToast />` at app level
- `_Imports.razor` — added `@using SwebKit.App.Components.Notifications`

## Remaining

- Integrate into Service Bus feature (message sent, resubmitted, scheduled cancelled) ✅
- Integrate into AKS feature (deployment restarted, pod deleted, scale, Helm rollback, YAML save, URL copy) ✅
- Integrate into Redis feature (key deleted, TTL updated, value saved, DB flushed) ✅
- Integrate into Storage feature (blob downloaded, SAS URL copied) ✅
- Integrate into Releases feature (approval submitted/rejected, deployment triggered) ✅
- Migrate / deprecate inline `ErrorCallout` usages incrementally
- Unit tests for `NotificationService`
- Manual E2E verification (toasts appear, auto-dismiss, bell badge clears)

## Blockers

None.

## Validation

Build: ✅ 0 errors, 6 pre-existing warnings (unrelated to this feature).
