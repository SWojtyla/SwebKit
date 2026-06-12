# Backend — Monitoring Alert Rules

## Overview

The backend spans four projects: `SwebKit.Core` (contracts, models, repository), `SwebKit.Kubernetes`, `SwebKit.Azure`, `SwebKit.Redis` (signal sources), and `SwebKit.App` (monitor engine, DI wiring, Windows notification).

---

## 1. Core Models (`SwebKit.Core/Models/MonitoringModels.cs`)

```csharp
public enum AlertRuleSource
{
    AksPodHealth,           // pod not ready / crash loop / terminated
    AksPodRestartRate,      // restart count exceeds threshold in window
    AksNamespaceHealthScore,// % not-ready pods exceeds threshold
    ServiceBusDlqDepth,     // DLQ message count above threshold
    ServiceBusActiveDepth,  // Active message count above threshold
    ServiceBusDeadSubscription, // DLQ growing + zero active msgs (consumer outage)
    RedisMemoryUsage,       // used-memory % above threshold
    RedisConnectedClients,  // client count drops to 0 or exceeds upper bound
    StorageBlobCount,       // blob count in container exceeds threshold
}

public enum AlertSeverity { Warning, Critical }

public sealed class MonitoringAlertRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public AlertRuleSource Source { get; set; }
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
    public int IntervalSeconds { get; set; } = 60;
    public int CooldownMinutes { get; set; } = 5;

    // Source-specific parameters (all nullable; only the relevant block is used)
    public AksPodAlertParams? AksPodParams { get; set; }
    public ServiceBusAlertParams? ServiceBusParams { get; set; }
    public RedisAlertParams? RedisParams { get; set; }
    public StorageAlertParams? StorageParams { get; set; }

    public DateTimeOffset? LastEvaluatedAt { get; set; }
    public DateTimeOffset? LastFiredAt { get; set; }
}

// Parameter bags — only the fields relevant to the rule source are populated
public sealed class AksPodAlertParams
{
    public string Namespace { get; set; } = string.Empty;  // "" = all namespaces
    public int RestartThreshold { get; set; } = 5;          // for RestartRate rule
    public double HealthScoreThreshold { get; set; } = 0.25; // 25 % not-ready
}

public sealed class ServiceBusAlertParams
{
    public string NamespaceConnectionAlias { get; set; } = string.Empty;
    public string EntityPath { get; set; } = string.Empty;  // queue or topic/subscription
    public long MessageCountThreshold { get; set; } = 1;
}

public sealed class RedisAlertParams
{
    public string ConnectionAlias { get; set; } = string.Empty;
    public double MemoryUsageThresholdPercent { get; set; } = 80.0;
    public int ClientCountLowerBound { get; set; } = 1; // for ConnectedClients rule
}

public sealed class StorageAlertParams
{
    public string AccountAlias { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public long BlobCountThreshold { get; set; } = 1000;
}
```

## 2. Alert Fired Event (`SwebKit.Core/Models/MonitoringModels.cs`)

```csharp
public sealed record AlertFiredEvent(
    string RuleId,
    string RuleName,
    AlertRuleSource Source,
    AlertSeverity Severity,
    string Message,          // short, toast-title-safe
    string Detail,           // longer human-readable context
    DateTimeOffset FiredAt,
    string ProfileName);
```

## 3. Signal Source Result (`SwebKit.Core/Models/MonitoringModels.cs`)

```csharp
public enum AlertSignalStatus
{
    Ok,       // condition not met — no alert
    Firing,   // condition met — emit alert
    Skipped,  // client not available or profile not configured — do not emit
    Error,    // evaluation threw; engine logs and continues
}

public sealed record AlertSignalResult(
    AlertSignalStatus Status,
    string? Message = null,
    string? Detail = null);
```

## 4. Abstractions (`SwebKit.Core/Abstractions/`)

### `IAlertSignalSource.cs`

```csharp
public interface IAlertSignalSource
{
    AlertRuleSource Source { get; }
    Task<AlertSignalResult> EvaluateAsync(MonitoringAlertRule rule, CancellationToken ct);
}
```

### `IAlertMonitorService.cs`

```csharp
public interface IAlertMonitorService : IAsyncDisposable
{
    bool IsMonitoring { get; }
    IReadOnlyList<AlertFiredEvent> RecentAlerts { get; }  // last 200

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();

    /// Raised on the thread pool; subscribers must InvokeAsync back to Blazor if updating UI.
    event Action<AlertFiredEvent>? AlertFired;
}
```

### `IAlertRuleRepository.cs`

```csharp
public interface IAlertRuleRepository
{
    Task<IReadOnlyList<MonitoringAlertRule>> GetAllAsync();
    Task SaveAllAsync(IReadOnlyList<MonitoringAlertRule> rules);
    Task<MonitoringAlertRule?> GetByIdAsync(string id);
    Task UpsertAsync(MonitoringAlertRule rule);
    Task DeleteAsync(string id);
}
```

## 5. Repository (`SwebKit.Core/Configuration/AlertRuleRepository.cs`)

- Persists to `%APPDATA%/SwebKit/monitoring-alerts.json` (path resolved via `AppDataPaths` helper, consistent with existing repositories).
- Uses the shared `SwebKitJsonContext` or a new `MonitoringJsonContext` for serialization.
- Writes atomically: write to `.tmp` then rename, same pattern as `ProfileRepository`.
- Holds no in-memory cache beyond the write-through list; callers re-read on page load.

## 6. Signal Source Implementations

### `AksPodAlertSignalSource` (`SwebKit.Kubernetes/AksClient/AksPodAlertSignalSource.cs`)

- Depends on `AppStateService` (reads active AKS client from DI; same bootstrapping as existing `PodHealthMonitorService`).
- Checks `IConnectionStateService.AksConnected`; returns `Skipped` if not connected.
- Calls `IAksClient.GetPodsAsync(rule.AksPodParams.Namespace)` and feeds result into the existing `PodHealthDiffer.Diff()` logic.
- Maintains per-rule snapshot state in a `Dictionary<string, Dictionary<string, PodSnapshot>?>` keyed by rule ID.
- Returns `Firing` with message `"Pod {name}: {eventType}"` when differ reports events.
- For `AksPodRestartRate` source: queries restart counts and fires if any pod exceeds `RestartThreshold`.
- For `AksNamespaceHealthScore`: computes `notReadyCount / totalCount` and fires if above threshold.

### `ServiceBusDlqSignalSource` (`SwebKit.Azure/ServiceBus/ServiceBusDlqSignalSource.cs`)

- Resolves the correct `IServiceBusClient` for the namespace alias via `AppStateService.GetServiceBusClient(alias)` (or equivalent injected factory pattern).
- Calls `IServiceBusClient.GetEntityRuntimePropertiesAsync(entityPath)` (add this method to the interface if not present, or use existing `GetEntityInfoAsync`).
- Checks `DeadLetterMessageCount > rule.ServiceBusParams.MessageCountThreshold`.
- Returns `Firing` with message `"DLQ: {count} messages on {entityPath}"`.

### `ServiceBusActiveDepthSignalSource` (`SwebKit.Azure/ServiceBus/ServiceBusActiveDepthSignalSource.cs`)

- Same resolution pattern as DLQ source.
- Checks `ActiveMessageCount > threshold`.

### `ServiceBusDeadSubscriptionSignalSource` (`SwebKit.Azure/ServiceBus/ServiceBusDeadSubscriptionSignalSource.cs`)

- Fires when `DeadLetterMessageCount > 0 AND ActiveMessageCount == 0` (backlog building, no consumers).

### `RedisMemorySignalSource` (`SwebKit.Redis/RedisMemorySignalSource.cs`)

- Resolves Redis connection via `AppStateService`.
- Calls `IRedisClient.GetMemoryInfoAsync()` (add if not present; simple `INFO memory` parse).
- Computes `used_memory / maxmemory * 100`; fires if above threshold.
- Falls back to `Skipped` when `maxmemory` is 0 (unlimited).

## 7. Alert Monitor Engine (`SwebKit.App/Services/AlertMonitorService.cs`)

### Key design points

- **Singleton** registered in `MauiProgram.cs` alongside other singletons.
- Starts automatically via `AppStateService.Initialized` event (same pattern as `PodHealthMonitorService`).
- Loads rules from `IAlertRuleRepository` on start; reloads when notified (event raised by `MonitoringPage` on save).
- Maintains one `PeriodicTimer` per enabled rule, or a single shared timer at the GCD interval — prefer single timer polling all due rules to avoid timer proliferation.
- **Concurrency:** `SemaphoreSlim` of capacity 4 limits simultaneous signal-source evaluations.
- **Cooldown:** per-rule cooldown tracked in `Dictionary<string, DateTimeOffset>` keyed by rule ID; fires only when `now > lastFiredAt + cooldown`.
- **History:** ring buffer (`_recentAlerts`, capacity 200) guarded by a lock.
- On `AlertFired`, calls `IWindowsNotificationService.ShowAlert(evt)` and `INotificationService.AddWarning/AddError` for in-app toast.

### `AppStateService` integration

- Subscribe to `AppStateService.Initialized` before starting the polling loop — same guard as `PodHealthMonitorService`.
- Do NOT start if no `IAksClient` is resolvable for AKS-type rules; those rules return `Skipped` individually.

## 8. Windows Notification Extension

### `IWindowsNotificationService` (`SwebKit.Core/Abstractions/IWindowsNotificationService.cs`)

Add:

```csharp
void ShowAlert(AlertFiredEvent evt);
```

Retain `ShowPodAlert(PodHealthEvent)` until `PodHealthMonitorService` removal is confirmed.

### `WindowsToastNotificationService` (`Platforms/Windows/WindowsToastNotificationService.cs`)

Implement `ShowAlert`:

- Title: `"{severity}: {evt.RuleName}"` (XML-escaped)
- Body: `evt.Message`
- Attribution: `"{evt.Source} · {evt.FiredAt:HH:mm:ss} · {evt.ProfileName}"`

## 9. Migration (`SwebKit.App/Services/MonitoringMigrationService.cs`)

Runs once at startup (after `AppStateService.Initialized`):

1. Check if `monitoring-alerts.json` exists. If yes, skip migration entirely.
2. Read `AppStateService.Config.AksConfig.MonitoredNamespaces`.
3. For each namespace, create a `MonitoringAlertRule` with:
   - `Source = AlertRuleSource.AksPodHealth`
   - `Name = "Pod health — {ns}"`
   - `AksPodParams = new { Namespace = ns }`
   - `IntervalSeconds = 60`, `CooldownMinutes = 5`, `Severity = AlertSeverity.Warning`
4. Save via `IAlertRuleRepository.SaveAllAsync(rules)`.

## 10. Deprecation of `PodHealthMonitorService`

- Remove `PodHealthMonitorService` and `IPodHealthMonitorService` registration from `MauiProgram.cs`.
- In `WindowsTrayLifecycleService`, replace `IPodHealthMonitorService.PodHealthDetected` subscription with `IAlertMonitorService.AlertFired`.
- Remove `PodHealthMonitorService.cs` from `SwebKit.App/Services/` (keep `PodHealthDiffer` and `PodHealthModels` — they are reused by `AksPodAlertSignalSource`).

## 11. DI Registration (`MauiProgram.cs`)

```csharp
// Repositories
builder.Services.AddSingleton<IAlertRuleRepository, AlertRuleRepository>();

// Signal sources (keyed or list-based resolution)
builder.Services.AddSingleton<IAlertSignalSource, AksPodAlertSignalSource>();
builder.Services.AddSingleton<IAlertSignalSource, ServiceBusDlqSignalSource>();
builder.Services.AddSingleton<IAlertSignalSource, ServiceBusActiveDepthSignalSource>();
builder.Services.AddSingleton<IAlertSignalSource, ServiceBusDeadSubscriptionSignalSource>();
builder.Services.AddSingleton<IAlertSignalSource, RedisMemorySignalSource>();

// Engine
builder.Services.AddSingleton<IAlertMonitorService, AlertMonitorService>();
builder.Services.AddSingleton<MonitoringMigrationService>();

// Remove: builder.Services.AddSingleton<IPodHealthMonitorService, PodHealthMonitorService>();
```

## 12. `IServiceBusClient` Gap

Check whether `IServiceBusClient` already exposes entity runtime properties (DLQ count, active count). If not, add:

```csharp
Task<ServiceBusEntityRuntimeInfo> GetEntityRuntimeInfoAsync(string entityPath, CancellationToken ct = default);
```

`ServiceBusEntityRuntimeInfo`:

```csharp
public sealed record ServiceBusEntityRuntimeInfo(
    long ActiveMessageCount,
    long DeadLetterMessageCount,
    long ScheduledMessageCount,
    DateTimeOffset UpdatedAt);
```

`AzureServiceBusClient` implements this via `ManagementClient.GetQueueRuntimePropertiesAsync` / `GetSubscriptionRuntimePropertiesAsync`.
