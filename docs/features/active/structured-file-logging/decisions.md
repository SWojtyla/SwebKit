# Decisions — Structured File Logging

## D1 — Hand-rolled provider instead of Serilog/NLog

**Decision:** Implement a small custom `ILoggerProvider`/`ILogger` on top of `Microsoft.Extensions.Logging` rather than adding a Serilog or NLog dependency.

**Why:** The app already uses `ILogger<T>` everywhere via DI — no call-site changes needed either way. The repo's established pattern for persistence-style concerns (`ProfileRepository`, `UiStateRepository`, `UserSettingsRepository`) is hand-rolled, dependency-free, and easy to unit test. Rolling + retention logic needed here is simple enough (~a few hundred lines) that a third-party dependency would add package-management and update overhead without a proportional benefit, and keeps the "lightweight" requirement literal (no extra assemblies shipped in the MAUI package).

**Revisit if:** Requirements grow to need multi-sink fan-out, structured query/search over logs, or something else Serilog already solves well.

## D2 — Feature buckets mirror `docs/architecture/functionalities/*.md`

**Decision:** Use the same names as the existing functionality docs (`service-bus`, `aks`, `redis`, `storage`, `devops`, `observability`, `incident-timeline`, `monitoring`, `api-client`) plus `agent` and a `general` fallback, instead of inventing a new taxonomy.

**Why:** Keeps a 1:1 mental model between "where a bug happens in the app" and "which log file to open." Also means future architecture-doc updates for a functionality area can note its log bucket name directly.

## D3 — Redaction is unconditional and applied at the logger boundary, not the call site

**Decision:** All redaction happens inside `FileLogger`/`LogRedactor`, never relying on call sites to pre-sanitize values before calling `_logger.LogInformation(...)`.

**Why:** Dozens of existing call sites already log operational messages (see `OAuth2TokenManager`, `ShellErrorPresenter`) without redaction in mind. Requiring every future call site to remember to scrub secrets is fragile and violates OWASP logging guidance (sensitive data must never reach a log sink). Centralizing redaction in the provider is the only reliable enforcement point.

**Follow-up:** Add a short note to `docs/pitfalls/dotnet-csharp.md` once implemented, warning that call sites should still avoid deliberately logging entire config/credential objects, since redaction is pattern-based and best-effort, not a guarantee against novel secret shapes.

## D4 — Logging preference lives in `user-settings.json`, not a new file

**Decision:** Add a `LoggingSettings` section to the existing `UserSettingsRepository` model instead of introducing a new persisted JSON file.

**Why:** Logging enabled/level is local-machine UI/runtime preference, exactly like the existing appearance settings already stored there. Avoids adding a fourth persistence file (`profiles.json`, `ui-state.json`, `user-settings.json`, `logging-settings.json`) for something this small, and reuses the already-audited atomic-write + backup-recovery path (`docs/pitfalls/dotnet-csharp.md` CS-4) instead of re-implementing it.

## D5 — Multi-instance write contention is an accepted v1 limitation (confirmed)

**Decision:** Do not add cross-process file locking for the log files. **Confirmed by the user — SwebKit runs as a single instance.**

**Why:** SwebKit is a single-window tray desktop app; concurrent manual launches are not a supported usage pattern. Adding OS-level file locking/mutex coordination for a debugging-aid feature would be disproportionate scope for v1.

## D7 — Superseded by D8 (kept for history)

The original design used size-based rolling (`feature.log.1`, `.2`, ...) plus a 24h marker file to gate cleanup. Replaced by the simpler daily-file design below, at the user's suggestion — it achieves the same crash-safety property with no marker state at all.

## D8 — One file per feature per calendar day; delete by date, not by marker

**Decision:** Each feature bucket writes to `logs/<feature>-<yyyy-MM-dd>.log` (local date), e.g. `service-bus-2026-07-08.log`. No numbered rolled files (`.1`, `.2`, ...). The writer detects a local-date change mid-session (app running across midnight) and switches to the new day's file automatically. `LogRetentionCleanupService` runs on **every** startup, parses the trailing date out of each `logs/*.log` filename, and deletes any file whose date is more than `MaxAgeDays` (default 7) before today. Files that don't match the expected `<feature>-yyyy-MM-dd.log` pattern are skipped, never deleted, out of caution.

**Why:** Today's file structurally can never be "older than `MaxAgeDays`" while it's the one being written to, so there is no scenario where running cleanup on every launch endangers the file a fresh crash just wrote to — no 24h gate or marker file is needed to achieve the same safety the earlier design worked harder for. It's also simpler to reason about on disk (`ls logs/` reads like a calendar) and simpler to implement (one deletion rule: `date < today - 7 days`).

## D9 — Per-day soft size cap as the remaining size safety net

**Decision:** Since daily files replace size-based rolling, each day's file still needs a bound so a runaway logging loop can't fill the disk within a single day. Each `DailyFileWriter` tracks its running byte count and enforces a soft cap (default 20 MB) per day-file. Once reached, further entries for that bucket are dropped for the rest of the calendar day (not rolled, not split into parts) and a single one-time line is appended noting the cap was hit, so it's visible rather than silently missing data.

**Why:** Keeps the "never becomes XXL" requirement satisfied without reintroducing rolling-file complexity — a new file starts automatically at midnight regardless, so the cap only needs to bound a single day's worst case.

## D10 — Crash-safe durability guarantee

**Decision:**

1. Any log entry at `Warning` level or above triggers an **immediate synchronous flush** of that entry's day-file right after it's written, instead of waiting for the normal batched flush interval. Only `Information`/`Debug`/`Trace` entries are batched.
2. `FileLoggerProvider` exposes an internal emergency path (`EmergencyWrite`/`EmergencyFlushAll`) that writes directly to the day-files, bypassing the bounded channel and its background drain task entirely.
3. Global crash handlers are registered once in `MauiProgram.cs`/`App.xaml.cs`: `AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`. Each handler synchronously calls the emergency path to record a `Critical` entry for the exception and flush all open day-files **before** the handler returns — they do not rely on the channel/background task, which may itself be the thing that's dying.

**Why:** The whole point of this feature is to have evidence after something goes wrong. A purely batched/async pipeline could lose exactly the lines that matter most (the error immediately preceding a crash) if the process dies before the next scheduled flush. Immediate flush on Warning+ plus a synchronous, channel-bypassing emergency path on unhandled exceptions closes that gap without forcing every `Information`-level line to pay a synchronous I/O cost.

## D6 — Defaults (confirm or adjust)

| Setting                         | Default                                                                                                                                                                                          |
| ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Minimum log level               | Warning (changed from Information — chosen for minimal overhead; only Warning/Error/Critical entries are captured by default, users can lower it in Settings → Diagnostics when troubleshooting) |
| Per-day-file soft size cap (D9) | 20 MB                                                                                                                                                                                            |
| Max age before deletion (D8)    | 7 days                                                                                                                                                                                           |
| Immediate flush threshold (D10) | Warning and above                                                                                                                                                                                |
| Enabled by default              | Yes                                                                                                                                                                                              |

These are called out explicitly so they can be tuned before implementation starts — none are hard-coded architectural constraints.
