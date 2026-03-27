# Decisions — AKS Pod Health Monitor

---

title: "Decisions — AKS Pod Health Monitor"
owner: ""
status: "Planned"

---

## Decision 001 — PeriodicTimer over Kubernetes Watch API

**Status:** Accepted

**Date:** 2026-03-26

### Context

Kubernetes offers a Watch API that streams pod status changes in real time via long-lived HTTP connections. This would give instant notification of pod failures. However, SwebKit's existing background work patterns all use `PeriodicTimer`-based polling (e.g., `AutoRefreshToggle.razor`), and the app does not have a hosted service infrastructure (`IHostedService` / `BackgroundService`).

### Decision

Use `PeriodicTimer` with a 2-minute polling interval, consistent with existing SwebKit patterns.

### Consequences

- **Enables:** Simple implementation, no persistent connection management, no reconnection logic, no WebSocket lifecycle concerns
- **Constrains:** Detection latency is bounded by the polling interval (~0–2 minutes). Not suitable for sub-second alerting.
- **Operational:** ~720 API calls per day per monitored namespace; well within Kubernetes API server limits for typical clusters

### Alternatives considered

- **Kubernetes Watch API** — rejected because it requires persistent HTTP connections, reconnection logic, bookmark handling, and a fundamentally different execution model than anything else in SwebKit. The added complexity is not justified for the 2-minute detection window.
- **Kubernetes Informer pattern** — rejected for similar reasons; informers are designed for controllers running in-cluster, not desktop apps.

---

## Decision 002 — Windows Toast Notifications via WinRT

**Status:** Accepted

**Date:** 2026-03-26

### Context

The existing `INotificationService` provides in-app toast notifications within the Blazor WebView. These are only visible when the app window is in the foreground and the user is looking at it. For pod failure alerts, the user may have the app minimized or be working in another application.

### Decision

Use WinRT `Windows.UI.Notifications.ToastNotificationManager` to send native Windows system toast notifications. These appear in the Windows notification center and as pop-up toasts regardless of app focus state. Supplement with in-app `INotificationService` for redundancy.

### Consequences

- **Enables:** Alerts visible even when app is minimized or unfocused; notifications persist in Action Center
- **Constrains:** Windows-only implementation; requires MSIX packaging (already satisfied by MAUI)
- **Platform code** lives in `Platforms/Windows/`, following existing pattern (`WindowsCredentialStore.cs`)
- **Future:** if macOS/Linux support is ever needed, the `IWindowsNotificationService` interface allows platform-specific implementations

### Alternatives considered

- **In-app only notifications** — rejected because the primary use case is background monitoring while the user works elsewhere
- **Third-party notification library (e.g., Notifications.Wpf)** — rejected; unnecessary dependency when WinRT APIs are directly available in MAUI Windows apps

---

## Decision 003 — Service-Level Monitoring (Not Component-Level)

**Status:** Accepted

**Date:** 2026-03-26

### Context

SwebKit's existing auto-refresh (`AutoRefreshToggle.razor`) is component-scoped: when the user navigates away from the AKS page, the timer is disposed and polling stops. For pod health monitoring, the user explicitly expects monitoring to continue when they navigate to Service Bus, Redis, or other pages.

### Decision

Implement monitoring as a singleton `PodHealthMonitorService` registered in DI, not as a Razor component. The service owns the `PeriodicTimer`, manages its own `CancellationTokenSource`, and publishes events via `IAppEventBus`. UI components subscribe to events for display but do not control the polling lifecycle.

### Consequences

- **Enables:** Monitoring survives page navigation; service is always available via DI
- **Constrains:** Service must manage its own AKS client lifecycle (since `IAksClient` is not in DI); must handle disposal carefully on app shutdown
- **Pattern:** Aligns with `ITaskQueue` and `INotificationService` — both are DI singletons that outlive individual page components

### Alternatives considered

- **Component-scoped timer in AksPage** — rejected because monitoring stops when the user navigates away, defeating the purpose
- **`BackgroundService` / `IHostedService`** — rejected because MAUI does not use the generic host; all background work is manual

---

## Decision 004 — 2-Minute Polling Interval

**Status:** Accepted

**Date:** 2026-03-26

### Context

The polling interval must balance detection speed against API load and system resource usage. The app runs on developer workstations, so CPU/network impact matters.

### Decision

Default polling interval of 2 minutes (120 seconds).

### Consequences

- **Detection latency:** 0–2 minutes from pod failure to notification (average ~1 minute)
- **API load:** ~720 calls/day per namespace at 2-minute intervals; negligible for typical AKS clusters
- **CPU:** `PeriodicTimer` is idle between ticks; minimal overhead
- **Future:** interval could be made configurable if users want faster/slower polling

### Alternatives considered

- **30 seconds** — rejected as unnecessarily aggressive for a desktop monitoring tool; would increase API calls 4x
- **5 minutes** — rejected as potentially too slow for critical pod failures that need prompt human attention
- **Configurable from day one** — deferred to keep initial scope focused; 2 minutes is a sensible default
