# Feature Overview — Global Notification System

---

title: "Global Notification System"
owner: ""
status: "Planned"
created: "2026-03-21"
updated: "2026-03-21"

---

## Goal

Introduce a global, reusable toast notification system that any feature can use to give the user feedback after completing actions — success confirmations, warnings, and errors — without requiring navigation away or modal interruption.

## Value

Currently, success feedback (message sent, deployment restarted, blob downloaded) is entirely absent. Errors surface via `ErrorCallout` components embedded in individual pages, which means failure is silent at the app level. A centrally managed toast layer provides consistent, non-blocking feedback across all features and raises the perceived quality of the whole application.

## Scope

### In scope

1. **`INotificationService`** — Singleton service with the following API:
   - `ShowSuccess(string message, string? detail = null)`
   - `ShowWarning(string message, string? detail = null)`
   - `ShowError(string message, string? detail = null, Exception? ex = null)`
   - `ShowInfo(string message, string? detail = null)`
   - Each call appends a `Notification` record to an internal list and fires a `NotificationsChanged` event.

2. **`NotificationToast.razor`** — Component rendered in `MainLayout` (above all content, outside the main content area). Displays up to 4 stacked toast cards at the top-right. Each card:
   - Has an icon and accent border colour matching severity (green/amber/red/blue using existing design tokens)
   - Shows message + optional detail (capped to 2 lines, expandable on click)
   - Auto-dismisses after 4 seconds (success/info) or 8 seconds (warning/error)
   - Has a manual close button (×)
   - Slides in from the right on appear, fades out on dismiss

3. **`NotificationHistory` panel** — Accessible from a bell icon in the `TopBar` (or via the status bar). Shows all notifications from the current session in reverse chronological order. Useful when a toast auto-dismissed before it was read. Cleared on app restart.

4. **Integration across all feature pages** — Replace ad-hoc `ErrorCallout` usages and add missing success feedback for:
   - Service Bus: message sent, message resubmitted, scheduled message cancelled
   - AKS: deployment restarted, pod deleted, port-forward started/stopped
   - Redis: key deleted, TTL updated, key value saved, database flushed
   - Storage: blob downloaded, SAS URL copied
   - Releases: approval submitted, deployment triggered

### Out of scope

- Persistent notifications across app restarts
- Notification grouping or deduplication
- Sound or OS-level toast notifications
- Action buttons within toasts (e.g. "Undo")

## Dependencies

- `MainLayout.razor` — host the `NotificationToast` component
- `TopBar.razor` — bell icon entry point for notification history
- All feature pages — inject and call `INotificationService`
- CSS design tokens: `--color-success`, `--color-warning`, `--color-error`, `--color-accent`
- `MauiProgram.cs` — register `INotificationService` as singleton

## Risks

- Notification volume: if multiple background operations complete simultaneously, stacked toasts could overwhelm the UI. Cap the visible stack at 4; queue the rest.
- Thread safety: `INotificationService` will be called from both UI threads and background tasks; internal list mutation must be thread-safe.
- `ErrorCallout` migration: existing pages use `ErrorCallout` inline. Migrating all of them in one pass risks regressions. Approach: introduce the service first, then migrate pages incrementally. Both patterns can coexist during transition.

## Related documents

- Architecture: `docs/architecture/architecture.md`, `docs/architecture/design.md`
- Pitfalls: `docs/pitfalls/blazor-maui.md`

## Quick links

- Status: `status.md`
- Backend plan: `backend.md`
- Frontend plan: `frontend.md`
- Test plan: `test-plan.md`
