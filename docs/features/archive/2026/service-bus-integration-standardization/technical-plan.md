# Service Bus Integration Standardization - Technical Plan

## Wave A: Infrastructure Refactor

### A1. Split `AzureServiceBusClient` responsibilities

Current `AzureServiceBusClient` mixes:
- Connection / client construction
- Admin operations (list queues/topics/subscriptions, set enabled/disabled)
- Runtime operations (peek, complete, purge, send, schedule)
- Stats operations
- Dead-letter processing

**Proposed split:**

```
SwebKit.Azure.ServiceBus/
  AzureServiceBusClient.cs          // thin facade implementing IServiceBusClient, delegates to services
  ServiceBusAdminOperations.cs      // queue/topic/subscription management
  ServiceBusMessageOperations.cs    // peek, send, receive, complete, purge
  ServiceBusStatsOperations.cs      // runtime properties and stats
  DeadLetterSequenceProcessor.cs    // keep, improve xml-doc
```

`IServiceBusClient` stays unchanged to avoid breaking consumers. `AzureServiceBusClient` becomes a delegating wrapper.

### A2. Remove constructor overload explosion

Current `AzureServiceBusClient` has 7 constructors. Standardize on:
- `AzureServiceBusClient(string connectionString, ServiceBusClientOptions, ILogger)`
- `AzureServiceBusClient(string fullyQualifiedNamespace, TokenCredential, ServiceBusClientOptions, ILogger)`
- Legacy config constructor remains for `ServiceBusClientFactory` compatibility.

Introduce `ServiceBusClientConnectionFactory` to centralize parsing of connection string / Entra credentials.

### A3. Centralize error handling and logging

Introduce a small `ServiceBusExceptionClassifier` to replace repeated `catch (OperationCanceledException) { throw; } catch { ... }` patterns.

## Wave B: UI Base Class Adoption

### B1. `ServiceBusPage`

- Add `@inherits SwebKit.App.Components.Shared.SwebKitComponentBase`
- Replace direct `InvokeAsync(StateHasChanged)` calls with `RequestRender()` / `RequestCoalescedRender()`
- Update `Dispose()` to use `new` + `base.Dispose()` pattern
- Decompose page into smaller subcomponents (optional, large file is 927 lines)

### B2. `ServiceBusGrid`

- Add `@inherits SwebKit.App.Components.Shared.SwebKitComponentBase`
- Remove hand-rolled `_cached*` sort/filter caches; rely on coalesced renders and memoized computed properties
- Replace all `InvokeAsync(StateHasChanged)` with `RequestRender()`
- Replace `IAsyncDisposable` with `IDisposable` and use base class disposal

### B3. `ServiceBusNamespacePanel`

- Audit for `StateHasChanged` patterns and apply base class coalescing if applicable

## Wave C: State & Cache Optimization

### C1. `ServiceBusGrid` filter/sort caching

Current implementation:
- Caches `_cachedFilteredSortedQueues`, `_cachedFilteredSortedTopics`
- Manual cache invalidation on `_filter`, `_sortCol`, `_sortAsc`, source list changes

Simplification:
- Use computed properties with `OrderBy`/`Where` executed on render
- Coalesce renders with `RequestCoalescedRender()` so rapid input doesn't flood Blazor
- If needed for large lists, add Virtualize to the grid rows

### C2. Stats loading throttling

Current `_statsGate` (`SemaphoreSlim(6,6)`) in `ServiceBusGrid` is reasonable but:
- Should be disposed
- Should honor component cancellation
- Should avoid multiple concurrent refreshes

Use `CancellationTokenSource` reset pattern from base class or a single `IAsyncCancelableOperation` helper.

## Wave D: Lifecycle & Disposal

### D1. Client ownership

`ServiceBusWarmupCache` stores `IServiceBusClient` instances:
```csharp
private readonly Dictionary<Guid, IServiceBusClient> _clients = [];
```

Issues:
- No disposal of old clients when replaced
- No disposal when cache is invalidated
- `IServiceBusClient` is not `IAsyncDisposable` (but `AzureServiceBusClient` is)

Fix:
- Change `IServiceBusClient` to extend `IAsyncDisposable`
- Update `ServiceBusWarmupCache` to track and dispose replaced clients
- Wire cleanup to `ServiceBusPage.Dispose()`

### D2. `ServiceBusGrid` disposal

Current `ServiceBusGrid`:
```csharp
public async ValueTask DisposeAsync()
{
    try { await _statsCts.CancelAsync(); } catch { }
    _statsCts.Dispose();
}
```

Missing `_statsGate.Dispose()`. Move to `Dispose()` using base class pattern.

## Wave E: Validation & Testing

### E1. Build verification

- Full solution build (or at least SwebKit.App + SwebKit.Azure)
- No compiler warnings

### E2. Functional validation

- Connect to Service Bus namespace
- List queues/topics/subscriptions
- Peek messages
- Send/schedule messages
- Complete/dead-letter workflows
- Verify no memory leaks or duplicate renders

### E3. Unit tests

- `ServiceBusClientConnectionFactory` parsing logic
- `ServiceBusExceptionClassifier` classification
- `DeadLetterSequenceProcessor` edge cases
- `ServiceBusWarmupCache` disposal behavior

## Files Expected to Change

- `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`
- `src/SwebKit.Azure/ServiceBus/ServiceBusClientFactory.cs`
- `src/SwebKit.Azure/ServiceBus/DeadLetterSequenceProcessor.cs`
- `src/SwebKit.Azure/ServiceBus/*.cs` (new)
- `src/SwebKit.Core/Abstractions/IServiceBusClient.cs`
- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- `src/SwebKit.App/Components/ServiceBus/ServiceBusGrid.razor`
- `src/SwebKit.App/Components/ServiceBus/ServiceBusNamespacePanel.razor`
- `src/SwebKit.App/Services/ServiceBusWarmupCache.cs`
