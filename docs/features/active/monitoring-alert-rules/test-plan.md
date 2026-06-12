# Test Plan — Monitoring Alert Rules

## Unit Tests — `SwebKit.Core.Tests`

### `AlertRuleRepositoryTests`

| #   | Scenario                        | Expected                                                |
| --- | ------------------------------- | ------------------------------------------------------- |
| 1   | Save and reload single rule     | Rule round-trips through JSON with all fields preserved |
| 2   | Save multiple rules, read all   | All rules returned in insertion order                   |
| 3   | Upsert existing rule by ID      | Rule is updated, not duplicated                         |
| 4   | Delete rule by ID               | Rule removed; other rules unaffected                    |
| 5   | Read from non-existent file     | Returns empty list without throwing                     |
| 6   | Concurrent writes (two upserts) | No file corruption; last write wins                     |

### `AlertSignalResult` / model tests

| #   | Scenario                                          | Expected                               |
| --- | ------------------------------------------------- | -------------------------------------- |
| 7   | Default `MonitoringAlertRule` has valid GUID `Id` | `Id` is a non-empty GUID string        |
| 8   | `AlertSeverity` serializes as string in JSON      | `"Warning"` / `"Critical"` (not 0 / 1) |
| 9   | `AlertRuleSource` serializes as string            | All enum values round-trip correctly   |

---

## Unit Tests — `SwebKit.App.Tests` (signal sources and engine)

### `AksPodAlertSignalSourceTests`

| #   | Scenario                                                | Expected                                                       |
| --- | ------------------------------------------------------- | -------------------------------------------------------------- |
| 10  | Connection state not connected                          | Returns `Skipped`                                              |
| 11  | First call with healthy pods                            | Returns `Ok` (no baseline yet — treated as baseline, no alert) |
| 12  | Pod transitions to CrashLoop between polls              | Returns `Firing` with message containing pod name              |
| 13  | Pod crash within cooldown window                        | Returns `Ok` (cooldown suppresses re-fire)                     |
| 14  | Namespace not found / 404 from API                      | Returns `Error`; source does not throw                         |
| 15  | `RestartRate` rule: restart count below threshold       | Returns `Ok`                                                   |
| 16  | `RestartRate` rule: restart count at or above threshold | Returns `Firing`                                               |

### `ServiceBusDlqSignalSourceTests`

| #   | Scenario                                    | Expected                               |
| --- | ------------------------------------------- | -------------------------------------- |
| 17  | DLQ count = 0, threshold = 1                | Returns `Ok`                           |
| 18  | DLQ count = 5, threshold = 1                | Returns `Firing` with count in message |
| 19  | DLQ count = 1, threshold = 5                | Returns `Ok`                           |
| 20  | Client not available (alias not configured) | Returns `Skipped`                      |
| 21  | SDK throws on `GetEntityRuntimeInfoAsync`   | Returns `Error`                        |

### `ServiceBusDeadSubscriptionSignalSourceTests`

| #   | Scenario            | Expected                        |
| --- | ------------------- | ------------------------------- |
| 22  | DLQ = 0, Active = 0 | Returns `Ok`                    |
| 23  | DLQ > 0, Active = 0 | Returns `Firing`                |
| 24  | DLQ > 0, Active > 0 | Returns `Ok` (consumers active) |

### `RedisMemorySignalSourceTests`

| #   | Scenario                    | Expected          |
| --- | --------------------------- | ----------------- |
| 25  | Memory 60 %, threshold 80 % | Returns `Ok`      |
| 26  | Memory 85 %, threshold 80 % | Returns `Firing`  |
| 27  | maxmemory = 0 (unlimited)   | Returns `Skipped` |
| 28  | Redis not connected         | Returns `Skipped` |

### `AlertMonitorServiceTests`

| #   | Scenario                                     | Expected                                                 |
| --- | -------------------------------------------- | -------------------------------------------------------- |
| 29  | Start with no rules                          | `IsMonitoring = true`; no evaluations run                |
| 30  | Single firing rule                           | `AlertFired` event raised; `RecentAlerts` contains event |
| 31  | Rule fires twice within cooldown             | Second fire suppressed; only one event in history        |
| 32  | Rule fires twice outside cooldown            | Both events in history                                   |
| 33  | Rule disabled                                | Evaluation skipped                                       |
| 34  | `StopAsync` called                           | Loop exits; no further events                            |
| 35  | Source returns `Skipped`                     | No `AlertFired` raised; no error logged                  |
| 36  | Source returns `Error`                       | Engine logs warning; continues polling other rules       |
| 37  | Max concurrency: 6 rules fire simultaneously | At most 4 evaluated in parallel; no exceptions           |
| 38  | History cap at 200                           | 201st event replaces oldest                              |

---

## Component Tests — `SwebKit.App.Tests`

### `MonitoringPageTests`

| #   | Scenario                     | Expected                                                    |
| --- | ---------------------------- | ----------------------------------------------------------- |
| 39  | Page renders with no rules   | "No alert rules configured" empty state visible             |
| 40  | Page renders with 3 rules    | All 3 rule rows visible; grouped correctly                  |
| 41  | Toggle global monitoring off | `StopAsync` called; status banner shows "Monitoring paused" |
| 42  | Toggle global monitoring on  | `StartAsync` called                                         |

### `AlertRuleListTests`

| #   | Scenario                        | Expected                                    |
| --- | ------------------------------- | ------------------------------------------- |
| 43  | Enable toggle for disabled rule | `OnToggle` callback invoked with rule       |
| 44  | Delete button on rule row       | `OnDelete` callback invoked                 |
| 45  | Edit button on rule row         | `OnEdit` callback invoked with correct rule |
| 46  | Add button                      | `OnAdd` callback invoked                    |

### `AlertRuleEditorTests`

| #   | Scenario                               | Expected                                            |
| --- | -------------------------------------- | --------------------------------------------------- |
| 47  | Open in create mode (Rule = null)      | Form renders blank; all required fields empty       |
| 48  | Open in edit mode with existing rule   | Form pre-populated with rule values                 |
| 49  | Source changed to `ServiceBusDlqDepth` | AKS fields hidden; Service Bus fields visible       |
| 50  | Save with empty Name                   | Validation error shown; `OnSave` not invoked        |
| 51  | Save with interval < 10                | Validation error; `OnSave` not invoked              |
| 52  | Save with valid data                   | `OnSave` invoked with correct `MonitoringAlertRule` |

### `AlertHistoryPanelTests`

| #   | Scenario                          | Expected                                    |
| --- | --------------------------------- | ------------------------------------------- |
| 53  | Empty history                     | Placeholder text rendered                   |
| 54  | Two events: Critical then Warning | Critical rendered first (newest first sort) |
| 55  | Clear button                      | `OnClear` callback invoked                  |

---

## Migration Tests

| #   | Scenario                                                                            | Expected                                       |
| --- | ----------------------------------------------------------------------------------- | ---------------------------------------------- |
| 56  | `AksConfig.MonitoredNamespaces` has 2 entries, no existing `monitoring-alerts.json` | 2 `AksPodHealth` rules created; file written   |
| 57  | `monitoring-alerts.json` already exists                                             | Migration skipped; file unchanged              |
| 58  | `AksConfig` is null                                                                 | Migration runs without error; no rules created |

---

## Manual Verification

| Check                                           | How                                                                                          |
| ----------------------------------------------- | -------------------------------------------------------------------------------------------- |
| Windows toast fires on pod crash                | Start monitoring with AKS rule; kill a test pod                                              |
| Toast fires for Service Bus DLQ                 | Manually enqueue to DLQ via existing DLQ tools; confirm toast                                |
| Tray unread count increments when hidden        | Minimize app; trigger an alert; verify tray badge                                            |
| Existing pod monitoring data migrated correctly | Configure `MonitoredNamespaces` in AKS config; launch app; open Monitoring tab; verify rules |
| Alert history shown in-session                  | Fire multiple alerts; confirm history list updates live                                      |
| Cooldown prevents repeated alerts               | Configure low interval; verify second fire is suppressed until cooldown expires              |
