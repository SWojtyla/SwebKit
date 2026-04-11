# Backend Plan - backend-reliability-hardening

---

title: "Backend Plan - backend-reliability-hardening"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Harden the backend paths behind DevOps, Service Bus, Redis, Observability, and app bootstrap so configuration is isolated, mutations are exhaustive, cursors are real, truncation is bounded, and failures are surfaced explicitly without widening the architecture.

## Impacted areas

- Existing projects and likely touchpoints:
- `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- `src/SwebKit.Core/Services/AppStateService.cs`
- `src/SwebKit.Core/Services/AppEventBus.cs`
- `src/SwebKit.DevOps/DevOpsClient.cs`
- `src/SwebKit.DevOps/DevOpsAuthHandler.cs`
- `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`
- `src/SwebKit.Redis/RedisClient.cs`
- `src/SwebKit.Observability/AzureAppInsightsProvider.cs`
- `src/SwebKit.App/MauiProgram.cs`
- minimal app adoption points such as `Pages/PipelinesPage.razor`, `Pages/DashboardPage.razor`, and `Pages/DevOpsConfigForm.razor`
- Likely new files only if the chosen design needs them:
- `src/SwebKit.Core/Abstractions/IDevOpsClientFactory.cs`
- `src/SwebKit.DevOps/DevOpsClientFactory.cs` or an equivalent configured client session type
- a small shared load-result or initialization-diagnostics model in `SwebKit.Core`

## Design

The design stays narrow and issue-driven.

1. DevOps configuration isolation
- Remove the current `Configure` plus `SetCredentialKey` mutable singleton pattern from the real DevOps path.
- Keep HTTP resilience in DI, but bind organization and PAT lookup to an immutable client snapshot or factory-created session.
- App consumers should continue using `IDevOpsClient`, but obtain a configured instance from the current environment instead of mutating a shared singleton.

2. Service Bus DLQ mutation correctness
- Rework DLQ complete and resubmit to mirror the existing multi-batch pattern already used by `CompleteMessagesAsync`.
- Continue receiving until the requested sequence set is empty or the broker is drained.
- Non-target messages received under lock should be abandoned predictably.
- If the broker is exhausted before all requested sequence numbers are found, surface that as an explicit failure rather than silent partial success.

3. Redis continuation correctness
- Replace the current `SetScanAsync` plus fabricated cursor arithmetic with source-backed `SSCAN` parsing or an equivalent raw-command approach.
- Keep `SetScanResult` as the page contract, but treat `Cursor` as opaque source state.
- Do not derive next cursor from `cursor + members.Count`.

4. Observability row-capping behavior
- Keep `RunQueryAsync` compatible with the existing provider contract.
- Bound row projection at `maxRows + 1` for truncation detection.
- Do not rewrite free-form user KQL automatically just to impose a cap.

5. ProfileRepository failure surfacing
- Replace swallow-and-reset behavior with an explicit load outcome that carries failure details safely.
- Keep startup non-fatal, but make the failure visible to `AppStateService`.
- Avoid any implementation path that would silently overwrite a corrupted `profiles.json` file on the next save.

6. AppEventBus dispatch semantics
- Keep `Publish` as sync-only.
- Keep `PublishAsync` as sync plus async.
- Distinguish handler types during dispatch so sync publish no longer logs `InvalidCastException` for async subscribers.

## API / Contracts

- Prefer additive contracts over breaking changes.
- Candidate additions:
- an `IDevOpsClientFactory` or equivalent app-owned creation boundary for real configured DevOps clients
- a small load-result or initialization-issue model for profile bootstrap diagnostics
- an explicit exception or result type for incomplete DLQ mutation only if existing method signatures cannot express the failure clearly enough
- Backward compatibility notes:
- keep the `IDevOpsClient` method surface stable for existing consumers where possible
- keep `IServiceBusClient` and `IRedisClient` signatures stable if behavior can be corrected without changing the contract
- preserve current DevOps HTTP resilience registration in `SwebKit.App`

## Tasks

### Wave 1 - Cross-cutting correctness contracts

- [ ] Define the profile-load failure contract and how `AppStateService` exposes initialization diagnostics.
- [ ] Update `ProfileRepository` so load failures are explicit and no longer silently treated as successful resets.
- [ ] Fix `AppEventBus` dispatch so `Publish` ignores async subscribers quietly and `PublishAsync` retains current mixed-handler behavior.
- [ ] Add or extend unit tests for repository diagnostics, `AppStateService` startup behavior, and `AppEventBus` logging behavior.

### Wave 2 - Integration client hardening

- [ ] Replace singleton mutable DevOps configuration with immutable per-configuration client creation.
- [ ] Update `DevOpsAuthHandler` to consume auth state without global mutable fields.
- [ ] Rework `AzureServiceBusClient` DLQ complete and resubmit loops to process all requested sequence numbers across batches.
- [ ] Replace Redis set-member fabricated cursor logic with source-backed cursor parsing.
- [ ] Cap `AzureAppInsightsProvider` row projection before full returned-model materialization.

### Wave 3 - Adoption, regression safety, and docs

- [ ] Update the app DI root and the minimal real-client callers to consume the corrected DevOps creation pattern.
- [ ] Add targeted DevOps and Service Bus regression tests in `tests/SwebKit.DevOps.Tests` and `tests/SwebKit.Azure.Tests`.
- [ ] Add targeted Core and App regression tests for profile load behavior, `AppEventBus` semantics, Redis continuation handling, and adoption safety.
- [ ] Add a direct observability truncation regression in the narrowest practical test target.
- [ ] Update the affected functionality docs in the same change set.

## Migration and runtime changes

- No infrastructure or data-schema migration is expected.
- Profile bootstrap behavior changes from silent fallback to explicit diagnostic; implementation must avoid destructive auto-save after a failed load.
- DevOps lifetime changes stay in `SwebKit.App` DI composition and caller acquisition only; no credential-store format change is required.

## Validation

- Unit tests: Not started
- Integration tests: Not started
- Manual checks:
- validate DevOps organization and PAT isolation across Dashboard, Pipelines, and DevOps settings flows
- validate DLQ actions against selected messages that cross the first receive batch
- validate Redis set-member Load More continuity until completion
- validate logs-tab truncation behavior with a deliberately low `maxRows` setting
- validate startup with a corrupted `profiles.json` file

## Notes

- Preserve current project boundaries: shared abstractions and services in `SwebKit.Core`, concrete client logic in integration projects, DI composition in `SwebKit.App`.
- Apply `docs/pitfalls/dotnet-csharp.md` guidance and do not swallow `OperationCanceledException` in new receive loops or query helpers.
- Apply `docs/pitfalls/azure-sdk.md` guidance to any new SDK enumeration or Service Bus paging behavior.
- Keep this feature narrow; do not expand into a generalized backend rewrite.