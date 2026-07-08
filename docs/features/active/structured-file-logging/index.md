# Structured File Logging

## Goal

Give SwebKit a lightweight, always-on, local-machine logging system that writes structured, per-feature log files to disk so operators (and the app author) can debug issues after the fact — without any network/telemetry dependency and without risking runaway disk usage.

## Quick Links

- Jira: not linked

## Scope

### What is built

- **`FileLoggerProvider` / `FileLogger`** — a custom, dependency-free `Microsoft.Extensions.Logging` provider registered via `builder.Logging.AddProvider(...)` in `MauiProgram.cs`, sitting alongside the existing `AddDebug()` provider. Captures the same `ILogger<T>` calls already used pervasively across the app (`WindowsTrayLifecycleService`, `OAuth2TokenManager`, `ShellErrorPresenter`, `MonitoringConnectionPool`, etc.) with zero call-site changes.
- **Feature-bucketed log files** — each log line is routed to a file named after the owning functionality area (mirroring `docs/architecture/functionalities/*.md`): `service-bus.log`, `aks.log`, `redis.log`, `storage.log`, `devops.log`, `observability.log`, `incident-timeline.log`, `monitoring.log`, `api-client.log`, `agent.log`, and `general.log` as the catch-all for shell/startup/settings code.
- **Structured NDJSON format** — one JSON object per line (timestamp, level, category, feature bucket, event id, rendered message, exception detail, flattened scope state) written with `Utf8JsonWriter`. No third-party logging library required.
- **Secret redaction** — messages, exceptions, and scope values are passed through a `LogRedactor` before serialization, masking connection strings, SAS/shared-access keys, bearer tokens, PATs, and any scope value whose key looks like `password`/`secret`/`token`/`key`/`connectionstring`/`pat`. This app persists Service Bus connection strings, DevOps PATs, Redis passwords, and Storage account keys — logging must never leak them.
- **Non-blocking background writes** — a bounded `System.Threading.Channels.Channel<LogEntry>` decouples logging calls from disk I/O; a single background drain task batches and flushes writes per feature file. Full channel drops the oldest entry rather than blocking the caller.
- **Size-capped rolling per feature file** — each feature file rolls to `.1`, `.2`, ... at a configurable max size (default 5 MB), keeping a bounded number of rolled files per feature (default 3).
- **Startup retention cleanup** — a background, best-effort cleanup task (mirroring the existing `MonitoringMigrationService` startup-task pattern) deletes rolled files older than a max age (default 14 days) and enforces a total `logs/` directory size cap (default 50 MB) as defense in depth beyond per-file rolling.
- **Settings toggle** — a small "Diagnostics" section added to `SettingsPage.razor`: enable/disable logging, minimum level (Warning default for minimal overhead, can be lowered to Information/Debug/Trace for deeper troubleshooting), "Open logs folder", and "Export logs as .zip" (for attaching to a bug report). Preference persisted as part of the existing local-machine `user-settings.json` (via `UserSettingsRepository`), consistent with how shell appearance preferences are stored.

### Non-goals

- Not a remote telemetry/log-shipping feature — App Insights (`SwebKit.Observability`) already covers cloud telemetry; this is purely local-machine files for support/debugging.
- Does not migrate `PerformanceBaselineRecorder` onto the new provider in this pass — left as a clean, optional follow-up (it could become a `performance` bucket).
- No in-app log viewer/tail UI in v1 — operators use "Open logs folder" or "Export as .zip"; an in-app viewer is a good future enhancement, explicitly deferred.
- Does not attempt cross-process file locking for multiple concurrent app instances — SwebKit is a single-window tray desktop app; multi-instance write contention is treated as an accepted, documented assumption rather than solved in v1.

## Dependencies

| Dependency               | Detail                                                                                                                                            |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AppDataPaths`           | Extended with a `LogsDirectory` and per-feature log file path helper, following the existing `Root`/override convention (`SWEBKIT_APPDATA_ROOT`). |
| `UserSettingsRepository` | Extended with a `LoggingSettings` section (enabled flag, minimum level) alongside existing appearance preferences.                                |
| `MauiProgram.cs`         | Registers the new logging provider early in `CreateMauiApp()` and schedules the startup retention cleanup as a fire-and-forget background task.   |
| `SettingsPage.razor`     | Hosts the new Diagnostics section (toggle, level picker, open-folder, export-zip actions).                                                        |

## Risks

| Risk                                                                                                         | Mitigation                                                                                                                                                                                                                                                        |
| ------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Secrets (connection strings, PATs, tokens) leak into log files.                                              | `LogRedactor` masks known secret patterns and denylisted scope-state keys before anything is serialized; covered by unit tests with real-shaped fixtures (Service Bus connection string, DevOps PAT, Bearer token).                                               |
| Logging becomes a performance/UI-thread bottleneck under high-frequency polling (Service Bus/AKS monitors).  | Bounded channel + background drain task; full channel drops oldest entry instead of blocking; default level is Warning, not Information/Debug/Trace, for minimal out-of-the-box overhead.                                                                         |
| Cleanup routine deletes evidence needed right after a crash (deleted on restart before it can be inspected). | Each feature file is named `<feature>-<yyyy-MM-dd>.log`; cleanup only ever deletes files whose date is more than 7 days old. Today's file can never match that rule, so cleanup is safe to run on every single startup — no gating needed. See `decisions.md` D8. |
| A crash happens before the last few log entries are flushed to disk.                                         | Warning/Error/Critical entries flush immediately (not batched); unhandled-exception/unobserved-task-exception handlers write the crash synchronously through an emergency path that bypasses the channel entirely. See `decisions.md` D10.                        |
| Cleanup routine throws or misbehaves and blocks startup.                                                     | Cleanup runs fire-and-forget after first idle, wrapped in try/catch that never surfaces to the UI; failures are swallowed (matching `PerformanceBaselineRecorder`'s own defensive try/catch).                                                                     |
| A single day's log volume grows unbounded (e.g. a logging loop bug).                                         | Per-day soft size cap (default 20 MB) per feature file; once hit, further entries for that bucket are suppressed for the rest of the day with a one-time visible marker line. See `decisions.md` D9.                                                              |
| Two app instances writing to the same feature file simultaneously.                                           | Not applicable — confirmed single-instance app (`decisions.md` D5).                                                                                                                                                                                               |
