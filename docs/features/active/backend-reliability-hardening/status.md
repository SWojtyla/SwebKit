# Status - backend-reliability-hardening

---

title: "Status - backend-reliability-hardening"
owner: "GitHub Copilot"
state: "Review"
jira: "not linked"
branch: ""
started: "2026-04-11"
last_updated: "2026-04-11"

---

## Quick summary

Implementation is landed and validated. This docs pass brings the backend feature and architecture notes up to date, and the feature is ready for review.

Jira: not linked

Current focus: reviewer confirmation only. No additional implementation is planned unless review uncovers a regression.

## Progress checklist

### Implementation

- [x] `ProfileRepository` returns `ProfileLoadResult` and blocks persistence after failed load
- [x] `AppStateService` surfaces profile-load diagnostics and blocked-save messaging
- [x] `MainLayout` shows a non-fatal profile-load warning banner
- [x] Real Azure DevOps callers create immutable snapshots through `IDevOpsClientFactory` / `DevOpsClientFactory`
- [x] `DevOpsAuthHandler` resolves the PAT credential key from per-request options instead of shared mutable state
- [x] `AzureServiceBusClient` routes DLQ complete/resubmit through `DeadLetterSequenceProcessor` across batches and fails explicitly for missing sequence numbers
- [x] `RedisClient.GetSetMembersPageAsync` uses `RedisScanResponseParser` and Redis-issued `SSCAN` cursors
- [x] `AzureAppInsightsProvider.RunQueryAsync` bounds row projection through `LogQueryResultProjector`
- [x] `AppEventBus.Publish` ignores async subscribers without logging false errors

### Validation and docs

- [x] Focused regression suites passed in `tests/SwebKit.Core.Tests`, `tests/SwebKit.DevOps.Tests`, and `tests/SwebKit.Azure.Tests`
- [x] Windows app build passed for the adopted app-layer changes
- [x] Backend feature docs and functionality docs now match the shipped behavior

## Completed

- Shipped the planned backend hardening seams across Core, DevOps, Azure, Redis, Observability, and minimal app adoption points.
- Preserved the current project boundaries and the existing Azure DevOps resilience registration.
- Converted the active feature docs from planning state to review-ready implementation notes.

## Remaining

- Reviewer feedback only.
- Archive or close out once review is complete.

## Blockers

- None.
- Jira is not linked. Informational only.

## Validation

- Test plan: `test-plan.md`
- Focused tests: 58 passed, 0 failed across the touched regression files in `tests/SwebKit.Core.Tests`, `tests/SwebKit.DevOps.Tests`, and `tests/SwebKit.Azure.Tests`
- App build: `dotnet build .\src\SwebKit.App\SwebKit.App.csproj -f net10.0-windows10.0.19041.0 --no-restore`
- Compiler state: no compiler errors reported on the touched backend and app adoption files

## Notes

- `AddStandardResilienceHandler` on the named Azure DevOps `HttpClient` remains unchanged; snapshot isolation happens at client creation and request-option flow.
- Profile persistence stays intentionally blocked after a failed profile load until the file is repaired.