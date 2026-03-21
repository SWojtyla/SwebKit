# Backend Refactor — Status

**Status:** Planned

## Progress checklist

### Phase 1 — Critical Bug Fixes
- [ ] Fix `PortForwardProcessRegistry` — remove `static` dictionary, move to instance-based registry in `KubernetesAksClient`
- [ ] Fix `AppInsightsObservabilityProvider.IsConnected` — make thread-safe with `volatile` or `Interlocked`
- [ ] Fix `DevOpsClient` thread-safety: `_orgUrl` / `_pat` fields should not be reassigned after construction, or use `Interlocked`

### Phase 2 — Error Handling & Logging
- [ ] Add `ILogger<T>` to `AppInsightsObservabilityProvider` — log swallowed exceptions at `Warning`
- [ ] Add `ILogger<T>` to `DevOpsClient` — log swallowed exceptions in `GetWaitingStagesAsync`, `GetRunStagesAsync`
- [ ] Add `ILogger<T>` to `RedisClient` — log swallowed exceptions in `TryGetMemoryUsageAsync`, `TryGetEncodingAsync`
- [ ] Add `ILogger<T>` to `AzureServiceBusClient` — log connection test failures
- [ ] Fix `TaskQueueService` fire-and-forget (`_ = Task.Delay(5000).ContinueWith(...)`) — use proper `async`/`await`
- [ ] Fix `AppEventBus.Publish<T>` — wrap each handler invocation in try/catch, log errors, continue remaining handlers

### Phase 3 — Missing Tests
- [ ] Add unit tests for `AzureServiceBusClient`: `PeekMessagesAsync`, `SendMessageAsync`, `ResubmitDeadLetterAsync`, entity path parsing
- [ ] Add unit tests for `AppInsightsObservabilityProvider`: `QueryLogsAsync` KQL building, `GetTraceAsync` span mapping
- [ ] Add unit tests for `KubernetesAksClient`: Helm release parsing, port-forward lifecycle, instance process registry
- [ ] Add unit tests for `RedisClient`: key scanning, type handling, TTL operations
- [ ] Add unit tests for `DevOpsClient`: `GetWaitingStagesAsync` state logic, approval flow mapping
- [ ] Add test utility `FakeCredentialStore` shared across all test projects

### Phase 4 — Configuration & DI
- [ ] Add validation to config classes (`ServiceBusConfig`, `ObservabilityConfig`, `AksConfig`, `RedisConfig`) — throw on missing required fields at construction time
- [ ] Fix `DevOpsClient.Configure()` anti-pattern — move to constructor injection or factory
- [ ] Extract shared `JsonSerializerOptions` to `SwebKit.Core` — remove 4 duplicated definitions
- [ ] Make `AppDataPaths` injectable (non-static) so tests can override paths

### Phase 5 — Code Quality
- [ ] Implement `GetMetricsAsync` in `AppInsightsObservabilityProvider` or mark as `throw new NotImplementedException()`
- [ ] Replace entity path string split in `AzureServiceBusClient` with explicit validation (`Split('/', 2)` + length check)
- [ ] Extract magic numbers to named constants: `524_288` (storage limit), `10_000` (log buffer), `500` (log tail), API version strings
- [ ] Extract generic `TryAsync<T>` helper to reduce boilerplate in `RedisClient`
- [ ] Refactor `GetWaitingStagesAsync` in `DevOpsClient` (~80 lines) into smaller private methods

## Blockers

None.
