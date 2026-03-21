# Backend Refactor — Archive Summary

**Completed:** 2026-03-21

## What was done

Paused new features to evaluate and improve backend code quality. No user-visible behaviour changed.

## Key changes

**Critical bug fixed** — `KubernetesAksClient` had a `static` process dictionary shared across all instances. Port-forward processes could leak across environments and were not cleaned up on crash. Moved to instance-level fields; `DisposeAsync` now kills all tracked processes.

**Error logging** — Added `ILogger<T>` to `AzureServiceBusClient`, `DevOpsClient`, `RedisClient`, and `AppEventBus`. Swallowed exceptions now log at `Warning`/`Error` rather than silently disappearing. `AppEventBus.Publish<T>` wraps each handler in try/catch so a throwing subscriber no longer kills remaining subscribers.

**Async fix** — `TaskQueueService` replaced `.ContinueWith()` with a proper `async` method.

**Code quality** — `SwebKitJsonOptions` extracted as a shared static (removed 4 duplicate definitions). `Limits.cs` named constants replace magic numbers. `TryAsync<T>` helper in `RedisClient` eliminates duplicated try/catch boilerplate. `GetWaitingStagesAsync` decomposed into 3 named helpers. Entity path parsing uses `Split('/', 2)` with `ArgumentException` on malformed input. `Validate()` added to all 4 config classes. `DevOpsClient.Configure()` guards against double-call.

**Tests** — 158 tests total (up from ~81), all passing. New coverage: `AppEventBus` subscriber-safety, `TaskQueueService` lifecycle, `AzureServiceBusClient` construction and path parsing, `KubernetesAksClient` static-state regression, `RedisClient` construction and config validation, and a new `SwebKit.DevOps.Tests` project with 24 tests including HTTP-level mocking for stage/approval flows.

## What was deferred

- `AppDataPaths` injectable abstraction (low priority)
- `DevOpsClient` factory pattern (current guard is sufficient; see decisions.md)
- Functional tests for `AzureServiceBusClient` send/peek/resubmit (requires wrapping sealed SDK types)
