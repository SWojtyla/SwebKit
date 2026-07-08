# Status — Structured File Logging

## Current State

`Review`

## Current Focus

All phases implemented and validated. Ready for manual smoke test in the running app, then pre-ship review.

## Completed

- [x] Feature plan created
- [x] Design finalized: daily-per-feature files (`<feature>-yyyy-MM-dd.log`), 7-day age-based cleanup with no marker needed, per-day soft size cap, crash-safe immediate flush + emergency write path for unhandled exceptions

## Remaining

- [x] **Phase 1 — Core diagnostics primitives** (`SwebKit.Core/Diagnostics`)
  - [x] `LogEntry` record
  - [x] `LogFeatureBucketResolver`
  - [x] `LogRedactor`
  - [x] `LoggingSettings` model
  - [x] `AppDataPaths.LogsDirectory` / `FeatureLogFile(feature, date)`
- [x] **Phase 2 — Daily file writer**
  - [x] `DailyFileWriter` (date-rollover detection, per-day soft size cap, immediate flush for Warning+, explicit directory + limits in constructor)
- [x] **Phase 3 — Logging provider**
  - [x] `FileLoggerProvider` (bounded channel, background drain task, per-bucket writer creation, `EmergencyWriteAndFlush`, graceful `Dispose`)
  - [x] `FileLogger` (category -> bucket resolution, redaction, scope flattening, `IsEnabled` respects `LoggingSettings`)
- [x] **Phase 4 — Retention cleanup**
  - [x] `ILogRetentionCleanupService` / `LogRetentionCleanupService` (parse date from filename, delete files older than 7 days, no marker)
- [x] **Phase 5 — Startup wiring and crash handlers** (`SwebKit.App`)
  - [x] `MauiProgram.cs`: register `FileLoggerProvider`, register cleanup service, schedule cleanup as fire-and-forget after startup
  - [x] `AppDomain.CurrentDomain.UnhandledException` handler wired to `EmergencyWriteAndFlush`
  - [x] `TaskScheduler.UnobservedTaskException` handler wired to `EmergencyWriteAndFlush`
- [x] **Phase 6 — Settings persistence and UI**
  - [x] `UserSettingsRepository` extended with `LoggingSettings`
  - [x] `DiagnosticsSettingsForm.razor` wired into `SettingsPage.razor`: enable toggle, minimum-level dropdown, open-logs-folder button, export-as-.zip button
- [x] **Phase 7 — Tests and docs**
  - [x] `LogFeatureBucketResolverTests`
  - [x] `LogRedactorTests`
  - [x] `DailyFileWriterTests`
  - [x] `LogRetentionCleanupServiceTests`
  - [x] `FileLoggerProviderTests` / `FileLoggerTests`
  - [x] Crash-safety tests (`CrashSafetyTests.cs` — emergency write path, unhandled-exception handler wiring, unobserved-task-exception handler wiring, redaction-under-crash, stalled-drain-task resilience — test-plan.md IDs 40-43)
  - [x] `docs/architecture/functionalities/settings-and-configuration.md` updated with the new Diagnostics section
  - [x] `docs/architecture/architecture.md` updated with the new "Structured file logging" cross-cutting concern row and local persisted state description
  - [x] `docs/architecture/codebase-guide.md` updated with entry-point rows for the logging engine, startup wiring, and Diagnostics settings UI
  - [ ] `docs/architecture/index.md` routing table — not updated; no dedicated logging deep-dive doc was warranted (cross-cutting concern, not a page-level functionality)

## Blockers

None.

## Validation Status

Full solution build (`dotnet build SwebKit.slnx`): succeeds, 0 errors. `SwebKit.Core.Tests`: 709/709 passing (703 baseline + 6 new `CrashSafetyTests`, covering test-plan.md IDs 40-43). `SwebKit.App` (net10.0-windows10.0.19041.0) builds clean with the `MauiProgram.cs` wiring and `DiagnosticsSettingsForm.razor`. `SwebKit.App.Tests` has 4 pre-existing failures unrelated to this change (confirmed via `git stash` against the base commit — `RedisKeyDetailTests` x2, `ShellFoundationTests.TopBar_ShowsConnectionStatusForCurrentRoute`, `AlertMonitorServiceTests.ReloadRulesAsync_PicksUpNewRules`).

All implementation and architecture-doc phases are complete. Remaining before archive: a manual smoke test in the running app (per test-plan.md "Manual / Smoke Verification") and a pre-ship review.
