# Test Plan - backend-reliability-hardening

---

title: "Test Plan - backend-reliability-hardening"
owner: "GitHub Copilot"
status: "Review"
created: "2026-04-11"
updated: "2026-04-11"

---

## Goal

Record the validation that was actually executed for the landed backend hardening work.

## Validated scope

- profile-load failure surfacing and blocked persistence
- non-fatal startup warning adoption in `MainLayout` (compile-validated)
- immutable DevOps client snapshot creation and PAT isolation
- exhaustive DLQ complete/resubmit batch processing
- source-backed Redis set-member continuation parsing
- bounded observability row projection and truncation detection
- sync-versus-async `AppEventBus` dispatch behavior

## Automated validation executed

- Orchestrator ran focused tests across `tests/SwebKit.Core.Tests`, `tests/SwebKit.DevOps.Tests`, and `tests/SwebKit.Azure.Tests`.
- Result: 58 passed, 0 failed.
- Key regression files:
- `tests/SwebKit.Core.Tests/AppStateServiceProfileLoadTests.cs`
- `tests/SwebKit.Core.Tests/AppEventBusTests.cs`
- `tests/SwebKit.Core.Tests/RedisScanResponseParserTests.cs`
- `tests/SwebKit.Core.Tests/LogQueryResultProjectorTests.cs`
- `tests/SwebKit.DevOps.Tests/DevOpsClientTests.cs`
- `tests/SwebKit.Azure.Tests/ServiceBus/DeadLetterSequenceProcessorTests.cs`

## Scenario coverage

1. Corrupted `profiles.json` yields `ProfileLoadResult.Failed`, startup remains non-fatal, and save stays blocked.
2. `SaveConfigAsync()` returns `false` instead of overwriting a corrupted profile file.
3. Real DevOps client snapshots keep separate organization and PAT values without cross-bleed.
4. DLQ processing continues across receive batches, releases non-target messages, throws for missing sequence numbers, and rethrows cancellation.
5. Redis scan parsing preserves the source cursor and only marks completion when Redis returns `0`.
6. Observability projection stops after `maxRows + 1` inspected rows and flags truncation correctly.
7. `AppEventBus.Publish()` ignores async-only subscribers without false error logging, while `PublishAsync()` still runs mixed handlers.

## Build and compiler validation

- App build passed: `dotnet build .\src\SwebKit.App\SwebKit.App.csproj -f net10.0-windows10.0.19041.0 --no-restore`
- No compiler errors were reported on the touched backend and app adoption files.

## Manual checks

- No additional manual checks were re-run as part of this docs-only close-out.
- Residual review risk is limited to shell presentation of the profile-load banner and save-status messaging, which were compile-validated but not re-executed in dedicated UI tests during this pass.

## Acceptance criteria status

- [x] Landed backend corrections are covered by focused regression tests or compile validation.
- [x] Persisted profile corruption is no longer silently overwritten.
- [x] DevOps live-client isolation is validated without shared mutable state.
- [x] DLQ mutations are exhaustive-or-fail.
- [x] Redis set-member paging uses source-backed continuation state.
- [x] Observability row projection truncates after `maxRows + 1` inspection.
- [x] Feature and architecture docs reflect the implementation.

## Validation status

- Automated: complete
- Build: complete
- Manual: not re-run for this docs close-out