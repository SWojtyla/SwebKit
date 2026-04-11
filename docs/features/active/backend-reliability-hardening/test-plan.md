# Test Plan - backend-reliability-hardening

---

title: "Test Plan - backend-reliability-hardening"
owner: "GitHub Copilot"
status: "Not started"
created: "2026-04-11"
updated: "2026-04-11"

---

## Goal

Validate that the backend hardening work removes silent failures, partial operations, fabricated pagination state, and false error logging while preserving the existing user workflows that depend on these services.

## Scope

- In scope: DevOps configuration isolation, DLQ multi-batch correctness, Redis set-member paging, App Insights truncation behavior, `ProfileRepository` load-failure surfacing, `AppEventBus` publish semantics, and the minimal app-layer adoption needed for these fixes.
- Out of scope: UI redesign, production-live Azure smoke testing, broad performance tuning outside row capping, and general cleanup of unrelated repositories or clients.

## Main scenarios (priority)

1. Scenario: two real DevOps client snapshots are created from different environment configs. Expected result: each request uses the correct organization URL and PAT without cross-bleed.
2. Scenario: a DevOps settings change occurs while another page still holds a previously configured client. Expected result: the older client continues using its original config and the new client uses the updated config.
3. Scenario: `CompleteDeadLetterAsync` targets sequence numbers that are not all present in the first broker receive batch. Expected result: the operation continues receiving until all requested messages are processed or the queue is exhausted.
4. Scenario: `ResubmitDeadLetterAsync` targets sequence numbers beyond the first broker receive batch. Expected result: all requested messages are resubmitted and completed, or the operation fails explicitly with the missing set.
5. Scenario: Redis set-member Load More spans three or more pages. Expected result: no duplicate or skipped members appear and the returned cursor remains source-backed.
6. Scenario: the final Redis set-member page is reached. Expected result: cursor becomes `0` and `IsComplete` becomes `true` only when the source page is complete.
7. Scenario: `RunQueryAsync` is called with a broad result set and a small `maxRows` value. Expected result: the provider returns at most `maxRows` rows and marks truncation without building returned models for every source row.
8. Scenario: `profiles.json` contains invalid JSON or incompatible content. Expected result: load failure is surfaced to the caller, startup remains non-fatal, and the failure is not silently treated as a successful reset.
9. Scenario: `AppEventBus.Publish` is called when only async subscribers are registered. Expected result: no `InvalidCastException` is logged and no async handler is invoked from sync publish.
10. Scenario: `AppEventBus.PublishAsync` is called with mixed sync and async subscribers. Expected result: sync handlers run, async handlers run, and exception behavior remains explicit and deterministic.
11. Scenario: cancellation is triggered during a DLQ receive loop or an observability query path. Expected result: `OperationCanceledException` is not swallowed.

## Automated coverage

- Unit tests: `tests/SwebKit.Core.Tests`
- Cover profile load results or initialization diagnostics, `AppStateService` startup behavior, `AppEventBus` sync and async dispatch, and any extracted Redis cursor helper logic.
- Client tests: `tests/SwebKit.DevOps.Tests`
- Cover organization and PAT isolation, sequential reconfiguration safety, and any factory or session construction logic for the real client.
- Integration-style client tests: `tests/SwebKit.Azure.Tests`
- Cover multi-batch DLQ completion and resubmit logic, missing-sequence failure behavior, and cancellation passthrough.
- App regression tests: `tests/SwebKit.App.Tests`
- Cover the minimal DevOps consumer adoption surface in Pipelines, Dashboard, and DevOps settings, plus Redis load-more behavior where the page depends on cursor continuity.
- Observability provider regression coverage:
- add a narrow direct test for `AzureAppInsightsProvider` row capping in the most practical adjacent test target
- CI gates:
- all affected suites pass and no regressions appear in existing DevOps, Service Bus, Redis, or app bootstrap flows

## Test data and setup

- Fake `HttpMessageHandler` and fake credential store entries for multiple DevOps configurations.
- Deterministic DLQ message fixtures whose target sequence numbers cross the first receive batch boundary.
- Redis set fixtures large enough to require repeated page fetches and expose duplicate or skipped members.
- Synthetic observability table data large enough to exceed `maxRows` by at least one row.
- Corrupted profile file fixtures and valid profile fixtures to verify non-destructive error handling.
- Test logger capture for `AppEventBus` to assert that sync publish no longer logs false cast failures for async handlers.

## Manual checks

- Check: DevOps environment switching safety. Steps: save or test one DevOps configuration, switch to another environment, open Dashboard and Pipelines, and confirm calls target the correct organization in each flow.
- Check: DLQ selected-message completeness. Steps: select dead-letter messages that would require more than one receive batch to reach, perform complete and resubmit, and confirm all selected items are processed.
- Check: Redis set Load More continuity. Steps: open a large set, load multiple pages, and confirm no duplicate or missing members appear before completion.
- Check: Observability truncation behavior. Steps: run a broad logs query with a deliberately small row cap and confirm truncation messaging appears while the page stays responsive.
- Check: corrupted profile startup. Steps: start with an invalid `profiles.json` file and confirm the app surfaces a warning or diagnostic path instead of silently resetting the profile state.

## Regression risks & mitigations

- Risk: DevOps lifetime changes break existing page consumers.
- Mitigation: keep the `IDevOpsClient` method surface stable and cover the app adoption points explicitly.
- Risk: DLQ receive-loop changes mishandle non-target locked messages.
- Mitigation: centralize the loop, abandon non-target messages predictably, and test across multiple batches.
- Risk: Redis paging correctness reveals unstable source order assumptions.
- Mitigation: validate continuity, not synthetic offset order.
- Risk: surfaced profile-load failures alter bootstrap behavior.
- Mitigation: keep startup non-fatal and document the new failure behavior.
- Risk: observability row capping changes logs-tab expectations.
- Mitigation: keep the provider contract stable and add a direct provider regression.

## Acceptance criteria

- All high-priority scenarios pass in automated or manual validation.
- No shared mutable PAT or organization state remains in the real DevOps path.
- DLQ complete and resubmit either process the full requested set or fail explicitly.
- Redis set-member paging uses source-backed continuation semantics and remains duplicate-free and skip-free across sequential pages.
- Profile load failures are surfaced to the caller and are not silently treated as successful resets.
- `AppEventBus.Publish` no longer logs false cast errors for async handlers.
- Tests and functionality docs are updated with the implementation.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Approved by:
- Date:
- Conditions (if any):