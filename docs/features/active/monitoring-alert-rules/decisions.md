# Decisions — Monitoring Alert Rules

## D-1 — Alert rules stored in a standalone file, not inside `AppConfig`

**Decision:** Use a dedicated `monitoring-alerts.json` file backed by `IAlertRuleRepository` rather than embedding `List<MonitoringAlertRule>` inside `AppConfig` / `profiles.json`.

**Rationale:**

- Alert rules are cross-profile operational configuration, not per-profile workspace state. A user running both a prod and dev profile should share the same alert rules (or at minimum be able to manage them independently of profile switching).
- `profiles.json` is already growing; adding a potentially large rule list would bloat it and complicate partial writes.
- Consistent with how `scheduled-messages.json` is already separated from `profiles.json`.

**Trade-off:** A standalone file means alert rules are not profile-scoped. If profile-scoped rules become a requirement later, the model can gain an optional `ProfileName` field and the repository can filter by it.

---

## D-2 — Single timer drives all rules; per-rule due-time tracking

**Decision:** `AlertMonitorService` uses a single `PeriodicTimer` at a fixed 10-second tick. Each rule stores its own `NextEvaluateAt` timestamp. On each tick, the engine evaluates all rules whose `NextEvaluateAt <= now` with bounded concurrency (max 4 parallel evaluations).

**Rationale:**

- Creating one `PeriodicTimer` per rule (N rules → N timers) creates resource pressure and complex cancellation management.
- A single timer with due-time tracking is the same pattern used by most scheduler implementations.
- The 10-second tick granularity is fine enough for any rule interval ≥ 10 seconds.

**Trade-off:** Minimum effective interval is 10 seconds regardless of rule configuration. Any rule configured < 10 s is clamped to 10 s.

---

## D-3 — `PodHealthDiffer` retained; `PodHealthMonitorService` removed

**Decision:** Keep `PodHealthDiffer` and `PodHealthModels` in `SwebKit.Core` as they are reused by `AksPodAlertSignalSource`. Remove `PodHealthMonitorService` (the AKS-specific orchestrator singleton).

**Rationale:**

- `PodHealthDiffer` is stateless diff logic with no AKS page coupling — it is correct to reuse it.
- `PodHealthMonitorService` is the singleton orchestrator that owns the polling loop, namespace list, and cooldowns. All of that is superseded by `AlertMonitorService` + `AksPodAlertSignalSource`.
- Removing `PodHealthMonitorService` without removing the differ allows a clean incremental migration.

**Migration path:** `WindowsTrayLifecycleService` currently subscribes to `IPodHealthMonitorService.PodHealthDetected`. After migration it subscribes to `IAlertMonitorService.AlertFired` and converts `AlertFiredEvent` to tray unread increment for alerts with an AKS source.

---

## D-4 — `IWindowsNotificationService` gains generic `ShowAlert`, retains `ShowPodAlert` during transition

**Decision:** Add `void ShowAlert(AlertFiredEvent evt)` to `IWindowsNotificationService`. Keep `void ShowPodAlert(PodHealthEvent evt)` until `PodHealthMonitorService` is fully removed and the method has no remaining callers.

**Rationale:**

- Immediately removing `ShowPodAlert` requires removing all call sites atomically. Adding `ShowAlert` first lets us migrate call-site by call-site and remove the old method in the same PR that removes `PodHealthMonitorService`.

---

## D-5 — Signal sources registered as `IEnumerable<IAlertSignalSource>`, resolved by `Source` enum

**Decision:** Register all `IAlertSignalSource` implementations as `IAlertSignalSource` in DI. `AlertMonitorService` receives `IEnumerable<IAlertSignalSource>` and builds a dictionary keyed by `AlertRuleSource` at construction.

**Rationale:**

- No keyed/named services needed (avoiding .NET 8 named-service complexity in MAUI).
- Adding a new signal source is a single `builder.Services.AddSingleton<IAlertSignalSource, NewSource>()` line — no switch/factory changes.
- The `Source` property on the interface acts as the discriminator key.

---

## D-6 — Alert rule editor uses discriminated param bags, not inheritance

**Decision:** `MonitoringAlertRule` carries four nullable parameter bag properties (`AksPodParams`, `ServiceBusParams`, `RedisAlertParams`, `StorageAlertParams`) rather than a sealed class hierarchy.

**Rationale:**

- Simpler JSON serialization — no `$type` discriminators needed.
- The editor already switches on `Source` to show/hide sections; the param bags match naturally.
- The rule set is not expected to become so large that OO polymorphism brings meaningful benefit.

**Trade-off:** Each new signal source category may require adding a new bag property to `MonitoringAlertRule`. Acceptable given the small number of categories.
