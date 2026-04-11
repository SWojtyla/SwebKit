# Backend - backend-reliability-hardening

---

title: "Backend - backend-reliability-hardening"
owner: "GitHub Copilot"
status: "Review"

---

## Goal

Document the backend reliability work that shipped so review can focus on the final seams and runtime behavior instead of the original plan.

## Landed backend seams

### 1. Profile load failure surfacing and safe persistence

- `ProfileRepository.LoadAsync()` now returns `ProfileLoadResult` with `NotFound`, `Loaded`, and `Failed` outcomes instead of silently resetting state.
- `ProfileRepository.TrySaveAsync()` and `SaveAsync()` now block persistence when the last load failed, preserving the existing `profiles.json` contents.
- `AppStateService` surfaces `ProfileLoadResult`, `HasProfileLoadFailure`, `IsProfilePersistenceBlocked`, and `ProfilePersistenceBlockedMessage` to app callers.
- `MainLayout` renders a non-fatal warning banner when startup recovered from a failed profile load, so the shell stays usable while the broken file remains untouched.

### 2. Azure DevOps immutable client snapshots

- `IDevOpsClientFactory` / `DevOpsClientFactory` is the app-owned seam for real Azure DevOps clients.
- `DevOpsClient` captures normalized organization input and `PatCredentialKey` at construction time; app callers no longer mutate a shared singleton with `Configure()`.
- `DevOpsAuthHandler` is stateless and reads `PatCredentialKeyOption` from `HttpRequestMessage.Options` for each request.
- `MauiProgram` keeps the existing named Azure DevOps `HttpClient` and resilience handler. `DashboardPage`, `PipelinesPage`, and `DevOpsConfigForm` create snapshots from the current `DevOpsConfig` when they need a live client.

### 3. Service Bus exhaustive DLQ mutation

- `AzureServiceBusClient.CompleteDeadLetterAsync()` and `ResubmitDeadLetterAsync()` now share `DeadLetterSequenceProcessor`.
- The processor keeps receiving across broker batches until every requested sequence number is matched or the dead-letter queue is drained.
- Non-target messages received under lock are released predictably.
- If any requested sequence numbers cannot be found, the operation fails explicitly and lists the missing values.

### 4. Redis source-backed set-member pagination

- `RedisClient.GetSetMembersPageAsync()` now issues raw `SSCAN` and routes the response through `RedisScanResponseParser`.
- `SetScanResult.Cursor` is now a Redis-issued continuation token, not a fabricated offset.
- `SetScanResult.IsComplete` becomes `true` only when Redis returns cursor `0`.

### 5. Observability bounded row projection

- `AzureAppInsightsProvider.RunQueryAsync()` still accepts free-form KQL and the existing `maxRows` contract.
- `LogQueryResultProjector` now materializes at most `maxRows + 1` rows, uses the extra row only to set `Truncated`, and returns at most `maxRows` rows to the UI.
- The provider no longer needs to project every returned row just to detect truncation.

### 6. Event-bus dispatch cleanup

- `AppEventBus.Publish()` now executes only synchronous subscribers and quietly skips async subscribers.
- `AppEventBus.PublishAsync()` continues to run both sync and async handlers.
- False `InvalidCastException` noise is gone, while real handler failures still log explicitly.

## Workstream completion

### Workstream 1 - Core state safety and startup diagnostics

- [x] Added explicit `ProfileLoadResult` outcomes to profile bootstrap.
- [x] Blocked profile persistence after failed load.
- [x] Surfaced profile-load warning state in `AppStateService` and `MainLayout`.
- [x] Cleaned up sync-versus-async `AppEventBus` dispatch behavior.

### Workstream 2 - Integration client hardening

- [x] Replaced shared mutable DevOps configuration with immutable client snapshots.
- [x] Moved DevOps PAT resolution to per-request options.
- [x] Made DLQ complete and resubmit exhaustive across receive batches.
- [x] Replaced fabricated Redis set-member continuation math with source-backed parsing.
- [x] Bounded observability row projection before truncation is finalized.

### Workstream 3 - Regression coverage and docs

- [x] Added or updated focused regressions in Core, DevOps, and Azure test projects.
- [x] Kept the app-layer adoption narrow to DI and live-client acquisition points.
- [x] Updated feature docs and functionality docs to describe the shipped runtime behavior.

## Validation

- Focused regression files of interest:
- `tests/SwebKit.Core.Tests/AppStateServiceProfileLoadTests.cs`
- `tests/SwebKit.Core.Tests/AppEventBusTests.cs`
- `tests/SwebKit.Core.Tests/RedisScanResponseParserTests.cs`
- `tests/SwebKit.Core.Tests/LogQueryResultProjectorTests.cs`
- `tests/SwebKit.DevOps.Tests/DevOpsClientTests.cs`
- `tests/SwebKit.Azure.Tests/ServiceBus/DeadLetterSequenceProcessorTests.cs`
- Focused suite result from orchestrator: 58 passed, 0 failed
- App build: `dotnet build .\src\SwebKit.App\SwebKit.App.csproj -f net10.0-windows10.0.19041.0 --no-restore`

## Notes

- No interface-wide rewrite was needed; the main structural seam added was `IDevOpsClientFactory`.
- The feature stays intentionally narrow and does not generalize the same pattern to every repository or every client.