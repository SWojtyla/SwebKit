# Test Plan — Global Notification System

## Validation strategy

Unit tests for `NotificationService`. Manual verification for toast rendering, animation, and auto-dismiss timing.

## Unit tests

- `ShowSuccess` adds a notification with `Success` severity
- `ShowError` adds a notification with `Error` severity and captures exception message
- `NotificationsChanged` event fires after each `Show*` call
- Notifications cap at 4 in the visible stack (excess queued)
- Dismissed notifications removed from visible list

## Main scenarios

### Toast appearance

| Scenario | Expected |
|---|---|
| `ShowSuccess` called | Green-accented toast slides in from right, auto-dismisses in 4s |
| `ShowError` called | Red-accented toast slides in, auto-dismisses in 8s |
| `ShowWarning` called | Amber-accented toast, auto-dismisses in 8s |
| `ShowInfo` called | Blue-accented toast, auto-dismisses in 4s |
| Toast has detail text | Detail shown (capped to 2 lines); expand on click |
| 5+ notifications fire quickly | 4 toasts visible; 5th queues and appears when one dismisses |

### Manual dismiss

| Scenario | Expected |
|---|---|
| User clicks × on toast | Toast dismissed immediately |
| User clicks × before auto-dismiss | Timer cancelled; no double-dismiss |

### Notification history

| Scenario | Expected |
|---|---|
| Bell icon clicked in TopBar | History panel opens |
| History panel open | All session notifications shown, newest first |
| App restart | History cleared |

### Feature integration

| Scenario | Expected |
|---|---|
| Service Bus: message sent | Success toast: "Message sent to [queue]" |
| AKS: deployment restarted | Success toast: "Deployment [name] restarted" |
| AKS: port-forward started | Info toast: "Port-forward active: localhost:XXXX → XXXX" |
| Redis: key deleted | Success toast: "Key deleted" |
| Any operation: client throws | Error toast with exception message |

## Regression risks

- `NotificationService` called from background threads — list mutations must be thread-safe
- Auto-dismiss timers must be cancelled on component disposal
- Existing `ErrorCallout` usage must remain functional during incremental migration

## Acceptance criteria

- Toast appears within 100ms of `Show*` call
- Auto-dismiss timing matches spec (4s success/info, 8s warning/error)
- History panel shows all session notifications
- Success feedback present for all listed feature actions
- No unhandled exceptions when notifications fire in rapid succession
