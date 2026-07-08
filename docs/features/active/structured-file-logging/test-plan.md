# Test Plan — Structured File Logging

## Unit Tests — `SwebKit.Core.Tests/Diagnostics`

### `LogFeatureBucketResolverTests`

| #   | Scenario                                                        | Expected                               |
| --- | --------------------------------------------------------------- | -------------------------------------- |
| 1   | Category `SwebKit.Azure.ServiceBus.AzureServiceBusClient`       | Resolves to `service-bus`              |
| 2   | Category `SwebKit.Kubernetes.AksClient.KubernetesAksClient`     | Resolves to `aks`                      |
| 3   | Category `SwebKit.Redis.RedisClient`                            | Resolves to `redis`                    |
| 4   | Category `SwebKit.Azure.Storage.AzureStorageClient`             | Resolves to `storage`                  |
| 5   | Category `SwebKit.DevOps.DevOpsClient`                          | Resolves to `devops`                   |
| 6   | Category `SwebKit.Observability.AzureAppInsightsProvider`       | Resolves to `observability`            |
| 7   | Category containing `IncidentTimeline`                          | Resolves to `incident-timeline`        |
| 8   | Category containing `Monitoring` or `Alert`                     | Resolves to `monitoring`               |
| 9   | Category `SwebKit.Agents.AgentChatService`                      | Resolves to `agent`                    |
| 10  | Category `SwebKit.App.Services.ShellErrorPresenter` (unmatched) | Resolves to `general` fallback         |
| 11  | Null/empty category                                             | Resolves to `general` without throwing |

### `LogRedactorTests`

| #   | Scenario                                                       | Expected                                                 |
| --- | -------------------------------------------------------------- | -------------------------------------------------------- |
| 12  | Message contains `Endpoint=sb://...;SharedAccessKey=abc123...` | `SharedAccessKey=` value replaced with `***REDACTED***`  |
| 13  | Message contains `AccountKey=...` (Storage connection string)  | Value redacted                                           |
| 14  | Message contains `Authorization: Bearer eyJhbGciOi...`         | Token redacted, `Bearer` prefix retained                 |
| 15  | Scope state contains key `Pat` with a token value              | Value replaced with `***REDACTED***` regardless of shape |
| 16  | Scope state contains key `Namespace` with a normal value       | Value passed through unchanged                           |
| 17  | Exception message/stack contains an embedded connection string | Redacted in the serialized exception text                |
| 18  | Plain message with no secret-shaped content                    | Passed through unchanged                                 |

### `DailyFileWriterTests`

| #   | Scenario                                                | Expected                                                                                               |
| --- | ------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| 19  | Append entries during a single day                      | All lines land in `<feature>-<today>.log`                                                              |
| 20  | Append after local date changes mid-session             | New entries land in `<feature>-<newDate>.log`; previous day's file untouched                           |
| 21  | Append pushes the day-file past `MaxDailyFileSizeBytes` | Further entries for that bucket suppressed for the rest of the day; one-time cap-reached line appended |
| 22  | Writer constructed against a fresh directory            | Directory is created if missing; no throw                                                              |
| 23  | Concurrent `AppendAsync` calls from multiple tasks      | No corrupted/interleaved lines; all entries eventually persisted                                       |
| 24  | Entry at `Warning` level or above appended              | File is flushed synchronously immediately after the write                                              |
| 25  | Entry at `Information` level appended                   | Write is buffered; flush happens on the normal batch interval, not immediately                         |

### `LogRetentionCleanupServiceTests`

| #   | Scenario                                                           | Expected                                                              |
| --- | ------------------------------------------------------------------ | --------------------------------------------------------------------- |
| 26  | File dated more than `MaxAgeDays` (7) before today                 | Deleted                                                               |
| 27  | File dated exactly `MaxAgeDays` before today                       | Retained (boundary is exclusive — only strictly older is deleted)     |
| 28  | File dated today                                                   | Never deleted, regardless of how many times cleanup runs              |
| 29  | Filename that doesn't match `<feature>-yyyy-MM-dd.log`             | Skipped, never deleted                                                |
| 30  | Cleanup invoked against a missing `logs/` directory                | No-op, does not throw                                                 |
| 31  | Cleanup run twice in a row (simulating two startups seconds apart) | Second run is a no-op beyond the first (idempotent; no marker needed) |
| 32  | Scan throws mid-way (e.g. file locked by AV) on one file           | Remaining files still processed; failure swallowed, no crash          |

### `FileLoggerProviderTests` / `FileLoggerTests`

| #   | Scenario                                                              | Expected                                                                    |
| --- | --------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| 33  | `LoggingSettings.Enabled = false`                                     | `IsEnabled(...)` returns `false` for all levels; no writes occur            |
| 34  | `LoggingSettings.MinimumLevel = Warning`, log called at `Information` | `IsEnabled(...)` returns `false`; entry not queued                          |
| 35  | Log call at or above minimum level                                    | Entry queued to channel and eventually appended to the correct feature file |
| 36  | Channel at capacity, new entry logged                                 | Oldest entry dropped; caller does not block or throw                        |
| 37  | `BeginScope` values present                                           | Flattened into `LogEntry.ScopeState`, redacted before serialization         |
| 38  | `Dispose()` called with pending entries in the channel                | Drain completes within the bounded timeout; entries are flushed to disk     |
| 39  | Exception passed to `LogError`                                        | Serialized exception type/message/stack present in the JSON line, redacted  |

### Crash-Safety Tests (`decisions.md` D10)

| #   | Scenario                                                                            | Expected                                                                        |
| --- | ----------------------------------------------------------------------------------- | ------------------------------------------------------------------------------- |
| 40  | `EmergencyWriteAndFlush` called directly with a crash `LogEntry`                    | Entry lands on disk synchronously without going through the channel             |
| 41  | `AppDomain.UnhandledException` handler invoked                                      | Calls `EmergencyWriteAndFlush` with a `Critical` entry containing the exception |
| 42  | `TaskScheduler.UnobservedTaskException` handler invoked                             | Calls `EmergencyWriteAndFlush`; `SetObserved()` is called                       |
| 43  | Emergency path invoked while the channel's background drain task is stalled/blocked | Emergency write still completes (it does not depend on the drain task running)  |

## Manual / Smoke Verification

- Run the app (`build-maui-windows` task), exercise Service Bus, AKS, and Settings pages, then confirm `%APPDATA%/SwebKit/logs/` contains the expected per-feature-per-day files (`service-bus-2026-07-08.log`, etc.) with valid NDJSON lines.
- Force a day-file past the soft cap (temporarily lower `MaxDailyFileSizeBytes` via config) and confirm suppression + the one-time cap-reached line.
- Toggle logging off in Settings and confirm no new lines are appended, but crash handlers still fire.
- Throw an unhandled exception deliberately (debug-only test hook) and confirm the exception appears in the relevant day-file even under a simulated stalled background task.
- Use "Export logs as .zip" and confirm the archive contains only the current `logs/` contents with no unredacted secrets when grepping the export for known test credential values.
