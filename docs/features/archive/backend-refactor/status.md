# Backend Refactor — Status

**Status:** Done

## Progress checklist

### Phase 1 — Critical Bug Fixes ✅
- [x] Fix `PortForwardProcessRegistry` — instance-level; `DisposeAsync` kills all tracked processes
- [x] Fix `DevOpsClient` thread-safety — `volatile` fields; `Configure()` guards against double-call

### Phase 2 — Error Handling & Logging ✅
- [x] Add `ILogger<AzureServiceBusClient>`
- [x] Add `ILogger<DevOpsClient>`
- [x] Add `ILogger<RedisClient>` + `TryAsync<T>` helpers
- [x] Add `ILogger<AppEventBus>` — per-handler try/catch, remaining handlers still fire on error
- [x] Fix `TaskQueueService` fire-and-forget — proper `async RemoveAfterDelayAsync()`

### Phase 3 — Tests ✅
- [x] `FakeCredentialStore` shared utility
- [x] `AppEventBus` tests (subscriber-throws, unsubscribe)
- [x] `TaskQueueServiceTests` — 17 tests
- [x] `AzureServiceBusClientParsingTests` — 10 tests
- [x] `KubernetesAksClient` static-state reflection test
- [x] `RedisClient` tests — 22 tests
- [x] `SwebKit.DevOps.Tests` — 24 tests (HTTP-level mocking, stage/approval mapping)
- [x] **158 tests total, all passing**

### Phase 4 — Configuration & DI ✅
- [x] `Validate()` on `ServiceBusConfig`, `AksConfig`, `RedisConfig`, `DevOpsConfig`
- [x] `DevOpsClient.Configure()` double-call guard
- [x] `SwebKitJsonOptions` shared options (4 duplicates removed)

### Phase 5 — Code Quality ✅
- [x] Entity path parsing validated (`Split('/', 2)` + `ArgumentException`)
- [x] `Limits.cs` named constants
- [x] `TryAsync<T>` / `TryValueAsync<T>` in `RedisClient`
- [x] `GetWaitingStagesAsync` decomposed into 3 helper methods

## Blockers

None.
