# Backend Refactor — Design & Implementation Notes

## 1. Critical Bug Fixes

### 1.1 `PortForwardProcessRegistry` — remove static state

**File:** `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs`

**Problem:** `_processes` is a `static Dictionary<Guid, Process>`. All `KubernetesAksClient` instances share one registry. Processes persist after the client is disposed. App crash leaves orphan processes.

**Fix:** Move registry fields to instance scope:

```csharp
// Before
private static readonly Dictionary<Guid, Process> _processes = [];
private static readonly Lock _lock = new();

// After (instance fields on KubernetesAksClient)
private readonly Dictionary<Guid, Process> _portForwardProcesses = [];
private readonly Lock _portForwardLock = new();
```

Remove the nested `PortForwardProcessRegistry` class. Update all callers.

In `DisposeAsync`, iterate and kill all tracked processes before clearing the dictionary.

### 1.2 `DevOpsClient` — mutable configuration fields

**File:** `src/SwebKit.DevOps/DevOpsClient.cs`

**Problem:** `_orgUrl` and `_pat` are set in `Configure()` which can be called from any thread.

**Fix (short-term):** Mark fields `volatile`. Document that `Configure()` must be called before any other method.

**Fix (preferred):** See §4 — move config to constructor.

---

## 2. Error Handling & Logging

### 2.1 Add `ILogger<T>` to all service implementations

All backend services must accept `ILogger<T>` via constructor injection. Swallowed exceptions must be logged at `Warning` or `Debug` level with context.

Pattern for "try" methods:

```csharp
private async Task<long?> TryGetMemoryUsageAsync(string key, CancellationToken ct)
{
    try
    {
        return (long?)await _db.ExecuteAsync("MEMORY", "USAGE", key);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "MEMORY USAGE failed for key {Key}", key);
        return null;
    }
}
```

Affected files:
| File | Logger name |
|------|------------|
| `DevOpsClient.cs` | `ILogger<DevOpsClient>` |
| `RedisClient.cs` | `ILogger<RedisClient>` |
| `AzureServiceBusClient.cs` | `ILogger<AzureServiceBusClient>` |

### 2.2 `AppEventBus.Publish<T>` — safe handler dispatch

**File:** `src/SwebKit.Core/Services/AppEventBus.cs`

**Problem:** If a subscriber throws, remaining subscribers never fire.

**Fix:**
```csharp
foreach (var h in handlers)
{
    try { ((Action<T>)h)(@event); }
    catch (Exception ex) { _logger.LogError(ex, "Event handler failed for {EventType}", typeof(T).Name); }
}
```

### 2.3 `TaskQueueService` — fix fire-and-forget

**File:** `src/SwebKit.Core/Services/TaskQueueService.cs`

Replace:
```csharp
_ = Task.Delay(5000).ContinueWith(_ => { ... });
```
With:
```csharp
_ = RemoveCompletedAfterDelayAsync(id);

private async Task RemoveCompletedAfterDelayAsync(Guid id)
{
    await Task.Delay(5000);
    lock (_lock) _tasks.RemoveAll(t => t.Id == id && t.Status != BackgroundTaskStatus.Running);
    TasksChanged?.Invoke();
}
```

(Still fire-and-forget, but with proper `async`/`await` — no `.ContinueWith` anti-pattern.)

---

## 3. Test Coverage

### 3.1 Testing strategy per service

All new unit tests use `xUnit` + `NSubstitute` (already in use in test projects). External dependencies (Azure SDK, Kubernetes client, StackExchange.Redis) are mocked via interfaces or wrapped behind thin adapters where needed.

### 3.2 `AzureServiceBusClient` tests

**File:** `tests/SwebKit.Azure.Tests/ServiceBus/AzureServiceBusClientTests.cs`

Test cases needed:

| Method | Scenario | Assert |
|--------|----------|--------|
| `PeekMessagesAsync` | Returns mapped messages | Message body, properties mapped |
| `PeekMessagesAsync` | Cancelled | `OperationCanceledException` propagated |
| `ResubmitDeadLetterAsync` | Happy path | DLQ message completed, new message sent |
| `SendMessageAsync` | Scheduled | `ScheduledEnqueueTime` set |
| Entity path parsing | `topic/subscription` | Correct split |
| Entity path parsing | Simple queue | No split attempted |

Strategy: Wrap `ServiceBusClient` behind `IServiceBusClientFactory` interface so it can be substituted in tests.

### 3.3 `KubernetesAksClient` tests

**File:** `tests/SwebKit.Kubernetes.Tests/KubernetesAksClientTests.cs`

Test cases needed:

| Method | Scenario | Assert |
|--------|----------|--------|
| Helm release parsing | Valid secret JSON | Release name, chart, version extracted |
| Helm release parsing | Malformed secret | Returns null, no throw |
| Instance process registry | Register + kill | Process killed on dispose |
| Instance process registry | Two clients | Separate registries, no cross-contamination |

### 3.5 `RedisClient` tests

**File:** `tests/SwebKit.Redis.Tests/RedisClientTests.cs` (new project)

Test cases needed:

| Method | Scenario | Assert |
|--------|----------|--------|
| `ScanKeysAsync` | Pattern match | Returns matching keys |
| `GetStringAsync` | Existing key | Returns value |
| `GetHashAsync` | Hash key | Returns field dictionary |
| `TryGetMemoryUsageAsync` | Command fails | Returns `null`, no throw |
| `DeleteAsync` | Multi-key | All keys removed |

Strategy: Wrap `IDatabase` behind `IRedisDatabase` interface.

### 3.6 `DevOpsClient` tests

**File:** `tests/SwebKit.DevOps.Tests/DevOpsClientTests.cs` (new project)

Test cases needed:

| Method | Scenario | Assert |
|--------|----------|--------|
| `GetWaitingStagesAsync` | Stages in waiting state | Returned in list |
| `GetWaitingStagesAsync` | Approvals API unavailable | Returns empty list, no throw |
| `ApproveStageAsync` | Success | HTTP PATCH called with correct body |
| `GetRunStagesAsync` | Exception | Returns empty list |

Strategy: Mock `HttpMessageHandler` via `MockHttpMessageHandler` or similar.

### 3.7 Shared test utilities

**File:** `tests/SwebKit.TestUtilities/FakeCredentialStore.cs`

```csharp
public sealed class FakeCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> _store = [];
    public void Save(string key, string value) => _store[key] = value;
    public string? Get(string key) => _store.GetValueOrDefault(key);
    public void Delete(string key) => _store.Remove(key);
}
```

Move from per-project duplicates to shared project referenced by all test projects.

---

## 4. Configuration & DI

### 4.1 Config validation

Add validation to all config records/classes. The appropriate place is a `Validate()` method or use constructor guards:

```csharp
public sealed class ObservabilityConfig
{
    public string WorkspaceId { get; init; } = "";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceId))
            throw new InvalidOperationException("ObservabilityConfig.WorkspaceId is required.");
    }
}
```

Call `Validate()` before passing config to service constructors.

### 4.2 `DevOpsClient` — remove `Configure()` anti-pattern

**Option A (constructor injection — preferred):**
Pass `DevOpsConfig` to `DevOpsClient` constructor. Re-create the client when config changes via a factory `IDevOpsClientFactory`.

**Option B (quick fix):**
Keep `Configure()` but make fields `readonly` after first assignment:
```csharp
public void Configure(DevOpsConfig config)
{
    if (_configured) throw new InvalidOperationException("Already configured.");
    _orgUrl = config.OrgUrl;
    _pat = config.PersonalAccessToken;
    _configured = true;
}
```

Decision recorded in [decisions.md](decisions.md).

### 4.3 Shared `JsonSerializerOptions`

**File:** `src/SwebKit.Core/Serialization/SwebKitJsonOptions.cs`

```csharp
public static class SwebKitJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };
}
```

Replace 4+ duplicated `JsonSerializerOptions` instances across `ProfileRepository`, `ReleaseRepository`, `UiStateRepository`, `DevOpsClient`.

### 4.4 `AppDataPaths` — make injectable

**File:** `src/SwebKit.Core/Configuration/AppDataPaths.cs`

Convert from `static` class to injectable `IAppDataPaths` interface + `DefaultAppDataPaths` implementation. Register in DI as singleton. Tests can substitute a temp-directory implementation.

---

## 5. Code Quality

### 5.1 `GetMetricsAsync` — mark as not implemented

**File:** `src/SwebKit.Azure/Observability/AppInsightsObservabilityProvider.cs`

```csharp
public Task<IReadOnlyList<MetricSeries>> GetMetricsAsync(MetricsQuery query, CancellationToken ct = default)
    => throw new NotImplementedException("GetMetricsAsync is not yet implemented.");
```

This surfaces the gap explicitly rather than silently returning empty results.

### 5.2 Entity path parsing — `AzureServiceBusClient`

Replace:
```csharp
var parts = entityPath.Split('/');
var topicName = parts[0];
var subName = parts[^1];
```
With:
```csharp
var parts = entityPath.Split('/', 2);
if (parts.Length != 2)
    throw new ArgumentException($"Invalid topic/subscription path: {entityPath}");
var (topicName, subName) = (parts[0], parts[1]);
```

### 5.3 Named constants

**File:** `src/SwebKit.Core/Constants/Limits.cs` (new)

```csharp
internal static class Limits
{
    public const int LogBufferMaxLines   = 10_000;
    public const int LogTailInitialLines =    500;
    public const int StoragePreviewBytes = 524_288; // 512 KB
    public const int TaskCompletionDelayMs = 5_000;
}
```

Replace magic numbers throughout.

### 5.4 `TryAsync<T>` helper — `RedisClient`

```csharp
private async Task<T?> TryAsync<T>(Func<Task<T>> action, string operationName) where T : class
{
    try { return await action(); }
    catch (Exception ex) { _logger.LogWarning(ex, "{Operation} failed", operationName); return null; }
}

// For value types:
private async Task<T?> TryValueAsync<T>(Func<Task<T>> action, string operationName) where T : struct
{
    try { return await action(); }
    catch (Exception ex) { _logger.LogWarning(ex, "{Operation} failed", operationName); return null; }
}
```

### 5.5 `GetWaitingStagesAsync` decomposition

Break the ~80-line method into:
- `FetchRunsAsync(pipelineId)` → raw run list
- `MapStageAsync(run, stage)` → `WaitingStage?`
- `FetchApprovalsAsync(run)` → approval list

Each method is independently testable.
