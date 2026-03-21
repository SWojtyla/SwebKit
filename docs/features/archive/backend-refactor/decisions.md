# Backend Refactor — Decisions

## Decision 1 — `DevOpsClient.Configure()` anti-pattern fix

**Date:** 2026-03-21

**Question:** `DevOpsClient` requires a manual `Configure(DevOpsConfig)` call after DI construction. How do we fix this?

**Options:**
1. Move `DevOpsConfig` to the constructor — break the singleton registration, use a factory
2. Keep `Configure()` but guard against double-call and race conditions
3. Use the Options pattern (`IOptions<DevOpsConfig>`) — requires config to be registered at app startup, not per-environment

**Decision:** Option 1 (factory pattern). `IDevOpsClientFactory` creates a new `DevOpsClient` per config. The factory is the DI singleton; the client is transient per environment. This matches how `AzureServiceBusClient` already works and is consistent with the rest of the codebase.

Option 3 is rejected because configs are per-project environment (loaded at runtime, not startup).

---

## Decision 2 — Wrapping Azure SDK types for testability

**Date:** 2026-03-21

**Question:** Azure SDK classes (`ServiceBusClient`, `LogsQueryClient`, etc.) are sealed and hard to mock. How do we test code that uses them?

**Options:**
1. Introduce thin wrapper interfaces (`IServiceBusSender`, `ILogsQueryClient`) around SDK types
2. Use `HttpMessageHandler` mocking (only works for HTTP-based clients like DevOps)
3. Use integration tests with Azure SDK test infrastructure / emulators

**Decision:** Option 1 for unit tests on business logic (entity mapping, KQL building, error handling). Option 3 for connectivity and happy-path integration tests (not in scope for this refactor — tracked separately). Option 2 for `DevOpsClient` only since it uses `HttpClient` directly.

Wrapper interfaces go in `SwebKit.Azure/Abstractions/` and are internal to the Azure project. They are not part of the public contract (`SwebKit.Core/Abstractions/`).

---

## Decision 3 — `AppDataPaths` injectable vs. environment variable override

**Date:** 2026-03-21

**Question:** `AppDataPaths` is a `static` class. Tests cannot override paths. How do we make it testable?

**Options:**
1. Convert to interface + DI singleton
2. Add an environment variable override (`SWEBKIT_DATA_DIR`) checked at startup
3. Keep static, use `AppDomain.SetData` hacks in tests

**Decision:** Option 1. `IAppDataPaths` with `DefaultAppDataPaths` (production) and `TempAppDataPaths` (tests using `Path.GetTempPath()`). This follows the pattern already established by `AppDataSandbox` in `SwebKit.Core.Tests`. Option 2 is added as a bonus for Docker/CI environments but is not required for this refactor.
