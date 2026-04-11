# Archive Summary - backend-reliability-hardening

---

title: "Archive Summary - backend-reliability-hardening"
owner: "GitHub Copilot"
jira: "not linked"
completed_date: "2026-04-11"
pr: "n/a"
commit: "n/a"

---

## Goal

Harden a narrow set of backend correctness and failure-handling paths so existing workflows operate on complete data, preserve persisted state safely, and surface recoverable problems explicitly.

## Delivered

- Changed profile bootstrap to return `ProfileLoadResult`, kept startup non-fatal, and blocked profile persistence after failed load so a corrupted `profiles.json` file is not silently overwritten.
- Surfaced profile-load warning and blocked-save state through `AppStateService` and the shell.
- Replaced mutable shared Azure DevOps client configuration with immutable per-config snapshots created through `IDevOpsClientFactory` and `DevOpsClientFactory`.
- Moved DevOps PAT lookup to per-request options in `DevOpsAuthHandler` so request authentication no longer depends on shared mutable state.
- Made Service Bus dead-letter complete and resubmit exhaustive across receive batches through `DeadLetterSequenceProcessor`, failing explicitly when requested sequence numbers cannot be found.
- Replaced fabricated Redis set-member continuation math with source-backed `SSCAN` parsing through `RedisScanResponseParser`.
- Bounded Application Insights row projection with `LogQueryResultProjector` and cleaned `AppEventBus.Publish()` so sync publish ignores async subscribers without false error logging.
- Added focused regression coverage and aligned the touched backend and architecture documentation.

## Key decisions

- Use immutable DevOps client snapshots instead of mutable singleton configuration.
- Treat DLQ mutation as exhaustive-or-fail rather than best-effort partial completion.
- Treat Redis continuation cursors as source-owned opaque tokens rather than synthetic offsets.
- Surface profile-load failures explicitly instead of swallowing them and resetting state.

## Validation performed

- Focused backend regressions passed across Core, DevOps, and Azure test projects: 58 passed, 0 failed.
- Windows app build passed: `dotnet build .\src\SwebKit.App\SwebKit.App.csproj -f net10.0-windows10.0.19041.0 --no-restore`.
- Touched backend and app-adoption files reported no compiler errors during verification.

## Lessons learned

- Several correctness bugs came from treating source-owned state such as cursors, receive windows, and auth/config snapshots as if local code could safely synthesize or reuse it.
- Non-fatal recovery is only trustworthy when the operator can see that startup degraded and persistence has changed behavior.
- Narrow reliability seams can remove high-value failure modes without widening into a full integration-layer refactor.

## Follow-up

- Add dedicated UI regression coverage for the profile-load warning and blocked-save messaging if that shell area changes again. Owner: unassigned.
- Keep future live DevOps callers behind `IDevOpsClientFactory` and avoid reintroducing `Configure()`-style mutable state. Owner: unassigned.

## Archive note

> This file is present because the feature had no Jira ticket (Path B). Archive location: `docs/features/archive/backend-reliability-hardening/`.