# Frontend Plan — Global Notification System

## Affected files

- `src/SwebKit.App/Components/Shared/NotificationToast.razor` — new
- `src/SwebKit.App/Components/Shared/NotificationToast.razor.css` — new
- `src/SwebKit.App/Components/Shared/NotificationHistory.razor` — new
- `src/SwebKit.App/Components/Layout/MainLayout.razor` — add `<NotificationToast />`
- `src/SwebKit.App/Components/Layout/TopBar.razor` — add bell icon + history toggle

## `NotificationToast.razor`

Renders in `MainLayout` outside the main content area. Positioned fixed at top-right.

Internals:
- Subscribes to `INotificationService.NotificationsChanged`
- Maintains a local `List<ToastEntry>` of currently visible toasts (max 4)
- Each `ToastEntry` wraps a `Notification` + a dismiss timer (`CancellationTokenSource`)
- New notifications from the service are added to the visible list; excess queued in a `Queue<Notification>`
- On dismiss (manual or timer): entry removed from list, next queued notification promoted

### Toast card layout

```
┌──────────────────────────────────────────┐
│ [Icon]  Message text                  [×]│
│         Detail text (optional, 2 lines)  │
└──────────────────────────────────────────┘
```

- Left accent border: 3px solid severity colour
- Icon: Fluent UI icon — checkmark circle (success), info (info), warning (warning), dismiss circle (error)
- Slide-in animation: CSS `@keyframes slideInRight` from `translateX(110%)` to `translateX(0)` over 200ms
- Fade-out: CSS `opacity: 0; transform: translateX(110%)` transition over 300ms on `.toast-dismissing` class

### Auto-dismiss timing

- Success / Info: 4000ms
- Warning / Error: 8000ms
- Timer starts after slide-in animation completes (`OnAfterRenderAsync` + `Task.Delay(200)`)

### Detail expand

- If `Detail` is non-null and truncated (> 2 lines), show "▼ Show more" toggle
- On click: expand to full detail text

## `NotificationHistory.razor`

Panel rendered conditionally in `TopBar.razor` (or as a dropdown below the bell icon).

Layout:
- Header: "Notifications" + "Clear all" button
- Empty state: "No notifications this session"
- List: all `INotificationService.All` in reverse order, each showing severity icon, message, relative time

## `TopBar.razor` changes

Add bell icon button to the right of the command palette button:
```html
<button class="bell-btn" @onclick="ToggleHistory" title="Notifications">
    <FluentIcon Value="@Icons.Regular.Size20.Alert" />
    @if (UnreadCount > 0) { <span class="badge">@UnreadCount</span> }
</button>
```

Bell shows a red badge dot when there are unread error/warning notifications. Count resets when history panel is opened.

## CSS notes (`NotificationToast.razor.css`)

```css
.toast-container {
    position: fixed;
    top: var(--spacing-lg);
    right: var(--spacing-lg);
    z-index: var(--z-overlay);
    display: flex;
    flex-direction: column;
    gap: var(--spacing-sm);
    pointer-events: none; /* container doesn't block clicks */
}
.toast-card {
    pointer-events: all;
    min-width: 280px;
    max-width: 400px;
    background: var(--color-surface);
    border: 1px solid var(--color-border);
    border-radius: 6px;
    padding: var(--spacing-sm) var(--spacing-md);
    box-shadow: 0 4px 16px rgba(0,0,0,0.4);
    animation: slideInRight 200ms ease-out;
}
.toast-card.toast-success { border-left: 3px solid var(--color-success); }
.toast-card.toast-info    { border-left: 3px solid var(--color-accent); }
.toast-card.toast-warning { border-left: 3px solid var(--color-warning); }
.toast-card.toast-error   { border-left: 3px solid var(--color-error); }
@keyframes slideInRight {
    from { opacity: 0; transform: translateX(110%); }
    to   { opacity: 1; transform: translateX(0); }
}
```

## Tasks

- [ ] Create `NotificationToast.razor` with queue, auto-dismiss, and slide animation
- [ ] Create `NotificationHistory.razor`
- [ ] Add bell icon + history toggle to `TopBar.razor`
- [ ] Add `<NotificationToast />` to `MainLayout.razor`
- [ ] Write CSS with animations and severity accent borders
- [ ] Verify animation performance (should use `transform`, not `top`/`left`)
- [ ] Test with 5+ rapid notifications (queue behaviour)
