# Backend Refactor — Status

**Status:** In Progress

## Progress checklist

### Phase 1 — Critical Bug Fixes ✅
- [x] Fix `PortForwardProcessRegistry` — removed `static` dictionary, moved to instance fields `_portForwardProcesses` + `_portForwardLock` on `KubernetesAksClient`; `DisposeAsync` kills all tracked processes
- [x] Fix `DevOpsClient` thread-safety — `_orgUrl` is now `volatile`; `Configure()` guards against double-call with `InvalidOperationException`

### Phase 2 — Error Handling & Logging ✅
- [x] Add `ILogger<AzureServiceBusClient>` — logs connection test failures at `Warning`
- [x] Add `ILogger<DevOpsClient>` — logs stage/approval fetch failures at `Warning`
- [x] Add `ILogger<RedisClient>` — logs Try* operation failures at `Warning`; added `TryAsync<T>` / `TryValueAsync<T>` helpers
- [x] Add `ILogger<AppEventBus>` — `Publish<T>` wraps each handler in try/catch, logs at `Error`, continues remaining handlers
- [x] Fix `TaskQueueService` fire-and-forget — replaced `.ContinueWith()` with proper `async RemoveAfterDelayAsync()`

### Phase 3 — Missing Tests ✅
- [x] Create `FakeCredentialStore` in `tests/SwebKit.Core.Tests/Fakes/`
- [x] Add `AppEventBus` tests: subscriber throws → others still fire; unsubscribe; no-subscribers
- [x] Fix pre-existing build break in `AzureClientGuardTests` (constructor signature update)
- [x] Create `TaskQueueServiceTests` — 17 tests covering enqueue/complete/cancel/clear lifecycle
- [x] Create `AzureServiceBusClientParsingTests` — 10 tests covering construction guards, entity path handling
- [x] Add `KubernetesAksClient` reflection test — verifies no static `Dictionary<,>` fields remain
- [x] Add `RedisClient` tests — 22 tests covering constructor guards, `RedisConfig.Validate()`
- [x] Create `SwebKit.DevOps.Tests` project — 24 tests covering `Configure()` guard, HTTP error handling, stage/approval mapping, pipeline queries
- [x] Add `DevOpsClient` to solution (`SwebKit.slnx`)
- **158 tests total, all passing ✅**

### Phase 4 — Configuration & DI ✅ (partial)
- [x] Add `Validate()` method to `ServiceBusConfig`, `AksConfig`, `RedisConfig`, `DevOpsConfig`
- [x] Fix `DevOpsClient.Configure()` anti-pattern — guard against double-call; field is `volatile`
- [x] Extract shared `SwebKitJsonOptions` to `SwebKit.Core/Serialization/SwebKitJsonOptions.cs` — used by `ProfileRepository`, `ReleaseRepository`, `UiStateRepository`, `DevOpsClient`
- [ ] Make `AppDataPaths` injectable — deferred
- [ ] Factory pattern for `DevOpsClient` — deferred (guard is sufficient for now, see decisions.md)

### Phase 5 — Code Quality ✅
- [x] Fix entity path parsing — `Split('/', 2)` + `ArgumentException` on malformed input
- [x] Create `SwebKit.Core/Constants/Limits.cs` — `LogBufferMaxLines`, `LogTailInitialLines`, `StoragePreviewBytes`, `TaskCompletionDelayMs`
- [x] Add `TryAsync<T>` / `TryValueAsync<T>` helpers in `RedisClient`
- [x] Decompose `GetWaitingStagesAsync` (~80 lines) into `ExtractWaitingStagesFromTimeline`, `BuildApprovalIdMap`, `EnrichWithApprovalsFallbackAsync`

## Remaining work
- `AppDataPaths` injectable — deferred (low priority)
- `DevOpsClient` factory pattern — deferred (see decisions.md)
- Functional tests for `AzureServiceBusClient` (peek, send, resubmit) — requires SDK wrapper interface, separate effort

## Blockers

None.
