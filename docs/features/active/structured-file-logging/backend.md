# Backend — Structured File Logging

## New Project Location

All engine code lives in `src/SwebKit.Core/Diagnostics/` (new folder) so it stays MAUI-free and directly unit-testable in `SwebKit.Core.Tests`, matching the existing pattern of `ProfileRepository`/`UiStateRepository` living in `SwebKit.Core.Configuration`.

```
src/SwebKit.Core/Diagnostics/
  LogEntry.cs                   -- immutable record: Timestamp, Level, Category, Feature, EventId, Message, Exception, ScopeState
  LogFeatureBucketResolver.cs   -- pure function: logger category string -> feature bucket name
  LogRedactor.cs                -- masks secret-shaped substrings/keys in message, exception text, and scope values
  LoggingSettings.cs            -- Enabled (bool), MinimumLevel (LogLevel), persisted via UserSettingsRepository
  DailyFileWriter.cs            -- per-feature-bucket writer targeting logs/<feature>-<yyyy-MM-dd>.log; auto-switches file at local-date rollover; enforces a per-day soft size cap; takes an explicit directory + limits in its constructor (NOT a static AppDataPaths call) for testability
  FileLoggerProvider.cs         -- ILoggerProvider; owns the bounded Channel<LogEntry> + background drain Task; creates one DailyFileWriter per feature bucket lazily; exposes the synchronous emergency write/flush path used by crash handlers
  FileLogger.cs                 -- ILogger implementation; formats state/exception, resolves feature bucket, redacts, posts LogEntry to the channel (or emergency-writes directly for Critical/unhandled-exception entries), never throws/blocks
  ILogRetentionCleanupService.cs / LogRetentionCleanupService.cs -- startup cleanup: parse date out of logs/*.log filenames, delete any file older than MaxAgeDays
```

## `AppDataPaths` Additions

```csharp
public static string LogsDirectory => Path.Combine(Root, "logs");
public static string FeatureLogFile(string feature, DateOnly date) =>
    Path.Combine(LogsDirectory, $"{feature}-{date:yyyy-MM-dd}.log");
```

Reuses the existing `Root`/`SWEBKIT_APPDATA_ROOT` override so tests can point logs at a temp directory exactly like other repositories do.

## Feature Bucket Resolution

`LogFeatureBucketResolver.Resolve(string category)` is an ordered list of prefix/substring predicates over the `ILogger<T>` category (the fully-qualified type name), mirroring `docs/architecture/functionalities/*.md`:

| Category match (examples)                      | Bucket               |
| ---------------------------------------------- | -------------------- |
| `SwebKit.Azure.ServiceBus.*`, `*ServiceBus*`   | `service-bus`        |
| `SwebKit.Kubernetes.*`, `*Aks*`                | `aks`                |
| `SwebKit.Redis.*`                              | `redis`              |
| `SwebKit.Azure.Storage.*`                      | `storage`            |
| `SwebKit.DevOps.*`                             | `devops`             |
| `SwebKit.Observability.*`                      | `observability`      |
| `*IncidentTimeline*`                           | `incident-timeline`  |
| `*Monitoring*`, `*Alert*`                      | `monitoring`         |
| `SwebKit.Agents.*`                             | `agent`              |
| `*ApiClient*`, `*Collection*Request*`          | `api-client`         |
| anything else (shell, startup, settings, tray) | `general` (fallback) |

Fallback to `general` must always succeed — an unmatched category is never an error.

## Redaction Rules (`LogRedactor`)

Applied to: rendered message text, exception `ToString()` output, and every scope-state value (from `BeginScope`).

- Regex-mask common secret shapes: `AccountKey=...`, `SharedAccessKey=...`, `SharedAccessSignature=...` (Azure Storage/Service Bus connection strings), `Authorization: Bearer <token>`, generic `[A-Za-z0-9\-_]{20,}` tokens following `pat=`/`token=`/`key=`/`secret=` (case-insensitive).
- Denylist scope-state keys (case-insensitive): `password`, `secret`, `token`, `key`, `connectionstring`, `pat`, `sas` — value replaced with `***REDACTED***` regardless of shape.
- Redaction runs unconditionally, even when the app-level logging toggle allows Debug level — there is no "trusted" log level that skips redaction.

## Write Pipeline (see `decisions.md` D8, D9, D10)

1. `FileLogger.Log(...)` builds a `LogEntry`, resolves its feature bucket, redacts, then:
   - For `Information`/`Debug`/`Trace`: calls `channel.Writer.TryWrite(entry)` — fully async, never blocks the caller.
   - For `Warning`/`Error`/`Critical`: also queued via the channel, but flushed synchronously through `DailyFileWriter` immediately after being drained (step 4) rather than waiting for the batch interval.
2. Channel is `Channel.CreateBounded<LogEntry>(capacity: 2000, FullMode = BoundedChannelFullMode.DropOldest)` — logging calls never block the caller (UI thread, polling background service, etc.).
3. `FileLoggerProvider` owns a single background `Task` that reads the channel, groups entries by feature bucket, and calls the matching `DailyFileWriter.AppendAsync(...)`.
4. `DailyFileWriter`:
   - Resolves today's file path (`AppDataPaths.FeatureLogFile(feature, DateOnly.FromDateTime(DateTime.Now))`) on every append; if the local date has changed since the last append, closes the previous day's stream and opens the new day's file.
   - Buffers `Information`/`Debug`/`Trace` writes and flushes periodically (time- or count-based); flushes immediately for `Warning`+ entries.
   - Tracks running byte count for the current day-file against `MaxDailyFileSizeBytes` (default 20 MB); once exceeded, drops further entries for that bucket for the remainder of the day and appends one one-time `"daily size cap reached, further <feature> entries suppressed until next day"` line so the omission is visible rather than silent.
5. On `FileLoggerProvider.Dispose()` (app shutdown/tray exit), the channel is completed and the drain task is awaited with a bounded timeout (e.g. 2s) so in-flight entries are flushed without hanging shutdown.

## Crash-Safe Emergency Path (see `decisions.md` D10)

`FileLoggerProvider` exposes:

```csharp
public void EmergencyWriteAndFlush(LogEntry criticalEntry);
```

This bypasses the channel and background drain task entirely — it resolves the entry's `DailyFileWriter` directly and performs a synchronous write + flush on the calling thread. Public (not `internal`) because the two global crash handlers live in `MauiProgram.cs`, a different assembly (`SwebKit.App`) than `SwebKit.Core`. Used only by those two handlers:

```csharp
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    var ex = e.ExceptionObject as Exception;
    fileLoggerProvider.EmergencyWriteAndFlush(LogEntry.ForCrash(ex, isTerminating: e.IsTerminating));
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    fileLoggerProvider.EmergencyWriteAndFlush(LogEntry.ForCrash(e.Exception, isTerminating: false));
    e.SetObserved();
};
```

Neither handler suppresses or alters the crash itself (`IsTerminating` unhandled exceptions still terminate the process as normal) — they only guarantee the exception is durably on disk before that happens. Both handlers are registered as early as possible in `CreateMauiApp()`, before any other startup work that could itself throw.

## Startup Wiring (`MauiProgram.cs`)

- Register the provider early, before other services that might log during construction:
  ```csharp
  builder.Logging.AddProvider(new FileLoggerProvider(loggingSettingsProvider));
  ```
- Register the two crash handlers immediately after constructing the provider (see above).
- Register `ILogRetentionCleanupService` in DI and schedule it as a fire-and-forget background task after `CreateMauiApp()` returns — same "perf startup" style already used by `PerformanceBaselineRecorder`/`MonitoringMigrationService` — so it never delays first paint.
- Respect the persisted `LoggingSettings.Enabled` flag: when disabled, `FileLoggerProvider` still exists (no DI churn) but `FileLogger.IsEnabled(...)` returns `false` for everything, so the pipeline is a no-op with negligible overhead. The crash handlers still fire regardless of the toggle — crash evidence is not something a user should be able to accidentally turn off.

## Retention Cleanup (see `decisions.md` D8)

`LogRetentionCleanupService`, run unconditionally on every startup (no marker file needed):

1. Enumerate `logs/*.log`.
2. Parse the trailing `-yyyy-MM-dd.log` suffix out of each filename. Files that don't match this exact pattern are skipped and left alone.
3. Delete any file whose parsed date is strictly before `today - MaxAgeDays` (default 7 days).
4. Wrapped in try/catch that swallows and never surfaces failures to the UI — a broken cleanup pass degrades to "no cleanup this run," never to "delete something unexpected."

Today's file is structurally exempt from every run (its date can never satisfy `date < today - 7 days`), so this is safe to run on every single launch, including immediately after a crash.

## Example Log Files On Disk

```
%APPDATA%/SwebKit/logs/
  service-bus-2026-07-08.log
  service-bus-2026-07-07.log
  aks-2026-07-08.log
  general-2026-07-08.log
```

`service-bus-2026-07-08.log` — one JSON object per line (NDJSON), keeps growing through the day until the soft cap or midnight:

```jsonl
{"ts":"2026-07-08T14:02:11.123Z","level":"Information","category":"SwebKit.Azure.ServiceBus.AzureServiceBusClient","feature":"service-bus","message":"Connected to namespace contoso-sb"}
{"ts":"2026-07-08T14:02:14.881Z","level":"Warning","category":"SwebKit.Azure.ServiceBus.AzureServiceBusClient","feature":"service-bus","message":"DLQ count 12 exceeds threshold 5 for queue orders-queue"}
{"ts":"2026-07-08T14:03:02.004Z","level":"Error","category":"SwebKit.Azure.ServiceBus.AzureServiceBusClient","feature":"service-bus","message":"Send failed for queue orders-queue","exception":{"type":"ServiceBusException","message":"...","stack":"..."}}
```

Redaction applies per-field before serialization, so a connection string embedded in a message becomes e.g. `"message":"Connection failed: Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=***REDACTED***"`.

## Settings UI

- New `DiagnosticsSettingsForm.razor` (or an inline new section in `SettingsPage.razor`, whichever keeps the accordion pattern consistent with existing sections like `DevOpsConfigForm.razor`).
- Controls: enable/disable toggle, minimum level dropdown (Information/Debug/Trace/Warning), "Open logs folder" (opens `AppDataPaths.LogsDirectory` in Explorer), "Export logs as .zip" (zips the current contents of `logs/` via `System.IO.Compression.ZipFile.CreateFromDirectory` into a user-chosen location or a default `Downloads`/temp path).
- Persistence goes through `UserSettingsRepository`, extending its existing model with a `LoggingSettings` property — same atomic-write/backup behavior already used for `user-settings.json` (see `docs/pitfalls/dotnet-csharp.md` CS-4).
