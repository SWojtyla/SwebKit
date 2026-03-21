# Backend Plan — Demo Mode Overhaul

## Audit: existing demo clients

Before writing new clients, audit method-by-method coverage of:

- `DemoAksClient` vs. current `IAksClient` interface
- `DemoRedisClient` vs. current `IRedisClient` interface

For each missing method, add a realistic implementation returning synthetic data (not `throw new NotImplementedException`).

## New demo clients

### `DemoServiceBusClient`

Implements `IServiceBusClient`. Returns synthetic data:

- **Namespaces**: 2 synthetic namespaces (`orders-dev`, `payments-dev`)
- **Queues**: 3 per namespace (e.g. `order-created`, `order-processed`, `order-failed`)
- **Topics**: 2 per namespace with 2 subscriptions each
- **Peek messages (active)**: 5 messages per entity with varied JSON bodies (order payloads, event envelopes) and realistic property sets (MessageId, CorrelationId, EnqueuedTime within last 24h)
- **Peek DLQ**: 3 DLQ messages per entity with `DeadLetterReason` and `DeadLetterErrorDescription` properties
- **Scheduled messages**: 2 scheduled messages per namespace with future `ScheduledEnqueueTime`
- **Send message**: no-op, returns success
- **Resubmit DLQ**: no-op, returns success
- **Cancel scheduled**: no-op, returns success
- **Complete / DeadLetter**: no-op

### `DemoStorageClient`

Implements `IStorageClient`. Returns synthetic data:

- **Accounts**: 2 accounts (`devstore`, `testblobs`)
- **Containers**: 3 per account (`configs`, `exports`, `fixtures`)
- **Blobs** (per container): 5–8 blobs with realistic names and paths
  - `configs/app-settings.json` — JSON content (sample app config)
  - `exports/2026-03-21-report.csv` — CSV-style text content
  - `fixtures/test-payload.json` — JSON event payload
- **Blob properties**: realistic `ContentType`, `ETag`, `LastModified`, `Size`
- **Blob content**: returns the sample content bytes for preview
- **SAS URL generation**: returns a fake `https://devstore.blob.core.windows.net/...?sv=demo` URL
- **Download**: no-op (writes empty file or sample bytes)

### `DemoReleasesClient`

Implements `IReleasesClient` (audit interface first). Returns synthetic data:

- **Pipelines**: 3 pipelines
  - `api-service`: last release `Succeeded`, deployed 2h ago
  - `worker-service`: last release `InProgress`, started 10m ago
  - `frontend`: last release `PartiallySucceeded`, 1 stage awaiting approval
- **Approvals**: 1 pending approval for `frontend` pipeline, step "Production Gate", pending 5m
- **Deployments**: list of 6 recent deployment records across pipelines
- **Tags**: 3 tags on the most recent release
- **Trigger deployment**: no-op, returns success
- **Submit approval**: no-op, returns success

## DI wiring

Update `MauiProgram.cs` to use demo clients when `AppState.UseDemoData = true`.

Since `UseDemoData` is a runtime toggle (not a build-time flag), the DI wiring must be dynamic. Options:

**Option A (recommended): Factory pattern**
Register a factory that reads `AppStateService.UseDemoData` at resolution time:
```csharp
builder.Services.AddSingleton<IServiceBusClient>(sp =>
    new DemoOrRealServiceBusClientFactory(sp));
```
Where the factory delegates to the real or demo implementation based on current state.

**Option B: Reload on toggle**
When `UseDemoData` changes, restart the relevant services or navigate away from the active feature page to force re-initialisation.

Decision between options: record in `decisions.md` after design review.

## Persist demo state

`UiStateRepository` JSON schema gains a `"useDemoData": bool` field. `AppStateService.InitializeAsync` reads this and sets `UseDemoData` accordingly. `ToggleDemo` in `TopBar` calls a new `AppStateService.SetDemoModeAsync(bool)` that saves to `UiStateRepository`.

## Affected files

- `src/SwebKit.Azure/Demo/DemoServiceBusClient.cs` — new
- `src/SwebKit.Azure/Demo/DemoStorageClient.cs` — new
- `src/SwebKit.Azure/Demo/DemoReleasesClient.cs` — new (or in appropriate project)
- `src/SwebKit.Azure/Demo/DemoAksClient.cs` — audit + fill gaps
- `src/SwebKit.Azure/Demo/DemoRedisClient.cs` — audit + fill gaps (or in `SwebKit.Kubernetes`)
- `src/SwebKit.App/MauiProgram.cs` — DI factory wiring for all clients
- `src/SwebKit.Core/Configuration/UiStateRepository.cs` — add `UseDemoData` field
- `src/SwebKit.Core/Services/AppStateService.cs` — `SetDemoModeAsync`, persist on toggle

## Tasks

- [ ] Audit `DemoAksClient` against `IAksClient` (fill all missing methods)
- [ ] Audit `DemoRedisClient` against `IRedisClient` (fill all missing methods)
- [ ] Implement `DemoServiceBusClient`
- [ ] Implement `DemoStorageClient`
- [ ] Implement `DemoReleasesClient`
- [ ] Design and document DI factory pattern (Option A vs B → `decisions.md`)
- [ ] Update `MauiProgram.cs` with factory wiring for all 5 clients
- [ ] Add `UseDemoData` persistence to `UiStateRepository` + `AppStateService`
- [ ] Unit tests: all demo clients implement interface without throwing
