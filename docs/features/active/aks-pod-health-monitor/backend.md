# Backend Plan — AKS Pod Health Monitor

---

title: "Backend Plan — AKS Pod Health Monitor"
owner: ""
status: "Not started"

---

## Goal

Implement a background pod health monitoring service that polls selected AKS namespaces, detects pod failures, and triggers Windows desktop notifications — all independent of UI component lifecycle.

## Impacted areas

- `src/SwebKit.Core/Abstractions/` — new interfaces
- `src/SwebKit.Core/Models/` — new models
- `src/SwebKit.App/Services/` — monitoring service implementation
- `src/SwebKit.App/Platforms/Windows/` — Windows toast notification service
- `src/SwebKit.App/MauiProgram.cs` — DI registration
- `src/SwebKit.Core/Models/AppConfig` area — config model extension

## Design

### Component Architecture

```
┌─────────────────────────────────────────────────┐
│               PodHealthMonitorService            │
│  (singleton, IAsyncDisposable)                   │
│                                                  │
│  ┌──────────────┐  ┌─────────────────────────┐  │
│  │ PeriodicTimer │  │ PodStateTracker          │  │
│  │ (2 min)       │  │ (per-namespace snapshots)│  │
│  └──────┬───────┘  └──────────┬──────────────┘  │
│         │                      │                  │
│         ▼                      ▼                  │
│  ┌──────────────┐  ┌─────────────────────────┐  │
│  │ IAksClient    │  │ NotificationCooldown     │  │
│  │ .GetPodsAsync │  │ (per-pod dedup tracker)  │  │
│  └──────────────┘  └──────────┬──────────────┘  │
│                                │                  │
│                     ┌──────────▼──────────┐      │
│                     │ IWindowsNotification │      │
│                     │ INotificationService │      │
│                     │ IAppEventBus         │      │
│                     └─────────────────────┘      │
└─────────────────────────────────────────────────┘
```

### Data flow per tick

1. Timer fires → iterate monitored namespaces
2. For each namespace: call `IAksClient.GetPodsAsync(ns)`
3. Compare current pod states against previous snapshot
4. Identify transitions: Running → Failed/CrashLoopBackOff/Unknown, container ready → not-ready, new crash restarts
5. For each detected issue → check cooldown tracker → if not suppressed → fire Windows toast + in-app notification + bus event
6. Update snapshot for next tick

## Contracts

### `IPodHealthMonitorService` (SwebKit.Core)

```csharp
public interface IPodHealthMonitorService : IAsyncDisposable
{
    bool IsMonitoring { get; }
    IReadOnlyList<string> MonitoredNamespaces { get; }

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();

    void AddNamespace(string ns);
    void RemoveNamespace(string ns);

    // Raised when a pod health event is detected
    event Action<PodHealthEvent>? PodHealthChanged;
}
```

### `IWindowsNotificationService` (SwebKit.Core)

```csharp
public interface IWindowsNotificationService
{
    void ShowPodAlert(PodHealthEvent evt);
    void ClearAll();
}
```

### Models (SwebKit.Core/Models)

```csharp
public sealed record PodHealthEvent(
    string PodName,
    string Namespace,
    string ClusterContext,
    PodHealthEventType EventType,
    string PreviousPhase,
    string CurrentPhase,
    int RestartCount,
    DateTimeOffset DetectedAt,
    string? Message = null);

public enum PodHealthEventType
{
    PodFailed,          // Phase → Failed
    PodCrashLoop,       // Phase → CrashLoopBackOff or restart count jumped
    PodUnknown,         // Phase → Unknown
    ContainerNotReady,  // Ready containers < total containers (was previously ready)
    PodTerminated       // Pod disappeared from namespace
}

public sealed class MonitoredNamespaceConfig
{
    public string ClusterContext { get; set; } = "";
    public List<string> Namespaces { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public int PollingIntervalSeconds { get; set; } = 120;
}
```

## Tasks

### Phase 1 — Core monitoring service

- [ ] **PHM-1** — Define `IPodHealthMonitorService` interface in `src/SwebKit.Core/Abstractions/`
- [ ] **PHM-2** — Define `PodHealthEvent`, `PodHealthEventType`, `MonitoredNamespaceConfig` in `src/SwebKit.Core/Models/`
- [ ] **PHM-3** — Implement `PodHealthMonitorService` in `src/SwebKit.App/Services/`
  - Singleton, `IAsyncDisposable`
  - Owns `PeriodicTimer` and `CancellationTokenSource`
  - Creates/recreates `IAksClient` based on current `AksConfig` (since it's not in DI)
  - Catches `OperationCanceledException` on timer cancellation
  - Catches transient errors (network, auth) — log warning, continue polling
  - Publishes `PodHealthEvent` via `IAppEventBus` and own event
- [ ] **PHM-4** — Pod state diffing logic (internal to service)
  - Maintain `Dictionary<string, PodSnapshot>` per namespace (keyed by pod name)
  - `PodSnapshot`: Phase, ReadyContainers, TotalContainers, RestartCount
  - Detect transitions:
    - Phase was `Running` → now `Failed`, `Unknown` → `PodFailed`, `PodUnknown`
    - Phase contains `CrashLoopBackOff` (in container waiting reasons) → `PodCrashLoop`
    - RestartCount increased since last snapshot → `PodCrashLoop`
    - Was ready (Ready == Total) → now not ready (Ready < Total) → `ContainerNotReady`
    - Pod was in previous snapshot → now missing → `PodTerminated`
  - Ignore pods that were already in a bad state at monitoring start (no alert on existing failures)
- [ ] **PHM-5** — Notification deduplication / cooldown
  - Per-pod cooldown: suppress duplicate alerts for the same pod+event type within a configurable window (default: 10 minutes)
  - Use `Dictionary<string, DateTimeOffset>` keyed by `"{namespace}/{podName}/{eventType}"`
  - Prune expired cooldowns periodically (piggyback on timer tick)
- [ ] **PHM-6** — Timer lifecycle
  - `StartAsync`: create `PeriodicTimer`, begin polling loop in `Task.Run`
  - `StopAsync`: cancel CTS, dispose timer, clear snapshots
  - `DisposeAsync`: call `StopAsync`
  - Guard re-entrance (don't start if already running)

### Phase 2 — Windows toast notifications

- [ ] **PHM-7** — Define `IWindowsNotificationService` in `src/SwebKit.Core/Abstractions/`
- [ ] **PHM-8** — Implement `WindowsToastNotificationService` in `src/SwebKit.App/Platforms/Windows/`
  - Use `Windows.UI.Notifications.ToastNotificationManager`
  - Get `ToastNotifier` for the app
  - Build toast XML from template
- [ ] **PHM-9** — Toast XML templates
  - Template includes: pod name, namespace, failure type, timestamp
  - Use `ToastGeneric` binding with hero text and attribution
  - Example:
    ```xml
    <toast activationType="foreground" launch="action=aksPage&amp;ns={namespace}">
      <visual>
        <binding template="ToastGeneric">
          <text>Pod Down: {podName}</text>
          <text>{namespace} — {eventType}</text>
          <text hint-style="captionSubtle">{clusterContext} · {timestamp}</text>
        </binding>
      </visual>
    </toast>
    ```
- [ ] **PHM-10** — Toast activation handling
  - When user clicks toast → bring app to foreground → navigate to AKS page with the relevant namespace
  - Handle via `ToastNotificationManagerCompat.OnActivated` or MAUI activation events

### Phase 3 — Config and persistence

- [ ] **PHM-11** — `MonitoredNamespaceConfig` model (defined in PHM-2)
- [ ] **PHM-12** — Extend config model
  - Add `MonitoredNamespaceConfig` to `AksConfig` or as a sibling config in `AppConfig`
  - Ensure serialization with `System.Text.Json`
- [ ] **PHM-13** — Config save/load
  - Persist on add/remove namespace
  - Load on service start; restore monitoring if `Enabled == true`

### Phase 4 — DI wiring and startup

- [ ] **PHM-18** — Register in `MauiProgram.cs`:
  - `services.AddSingleton<IPodHealthMonitorService, PodHealthMonitorService>()`
  - `services.AddSingleton<IWindowsNotificationService, WindowsToastNotificationService>()` (Windows-only conditional)
- [ ] **PHM-19** — Wire `IAppEventBus` events: `PodHealthEvent` published by service, consumed by UI components
- [ ] **PHM-20** — Auto-start monitoring
  - After app initialization, if monitored namespaces are configured and enabled, call `StartAsync`
  - Hook into existing `AppStateService.Initialized` event or `MainLayout` init flow

## Validation

- Unit tests: Not started
- Integration tests: Not started
- Manual checks:
  - Start monitoring for a namespace → kill a pod via `kubectl delete pod` → verify toast appears within 2 minutes
  - Verify toast click navigates to AKS page
  - Verify monitoring continues after navigating to Service Bus page and back
  - Verify monitoring stops cleanly on app close

## Notes

- **AKS client lifecycle:** Since `IAksClient` is not in DI, the monitor service must create its own client instance using the current `AksConfig`. When the user switches cluster contexts, the service should detect this (via `IAppEventBus` config change event) and recreate the client.
- **PeriodicTimer disposal:** Always dispose in `DisposeAsync`. Catch `OperationCanceledException` in the polling loop — this is the normal shutdown path.
- **Thread safety:** The service is a singleton accessed from both the timer callback and UI threads. Use `SemaphoreSlim` or `lock` for snapshot state. Notification dispatch should be fire-and-forget from the timer thread.
- **Pitfall BL-2:** Any `StateHasChanged()` triggered by service events must go through `InvokeAsync` in consuming components.
