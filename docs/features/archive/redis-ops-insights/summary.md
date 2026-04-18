# Archive Summary - redis-ops-insights

---

title: "Archive Summary - redis-ops-insights"
owner: "GitHub Copilot"
jira: "not linked"
completed_date: "2026-04-18"
pr: ""
commit: ""

---

## Goal

Extend the existing Redis page into an operator-grade diagnostics workspace exposing slow-command evidence, hot-key pressure signals, and Pub/Sub channel activity without leaving the current Redis page or introducing invasive server monitoring.

## Delivered

- **Wave 1 — Slowlog and hot-key evidence:**
  - `RedisInsightCapability` enum (`Loaded`, `Partial`, `Unsupported`, `PermissionLimited`, `Failed`) — used by both waves.
  - `RedisSlowLogEntryInfo`, `RedisSlowLogSummary` models; `GetSlowLogAsync` on `IRedisClient`, `RedisClient` (StackExchange.Redis `SLOWLOG GET`), and `DemoRedisClient`.
  - `RedisHotKeySignal`, `RedisHotKeySummary` models; `RedisOpsInsightsAggregator` service — correlates slowlog frequency, `OBJECT FREQ` (LFU), and low-idle-time signals into merged, explanation-annotated hot-key findings.
  - `RedisSlowLogPanel.razor` — slowlog table, capability badges, hot-key signal list with drill-through to key detail; empty and unsupported states.
  - 5 `RedisOpsInsightsAggregatorTests` (unit) + 5 `RedisSlowLogPanelTests` (bUnit).

- **Wave 2 — Pub/Sub visibility:**
  - `RedisPubSubChannelInfo`, `RedisPubSubSnapshot` models; `GetPubSubSnapshotAsync` on `IRedisClient`, `RedisClient` (`PUBSUB CHANNELS`, `PUBSUB NUMSUB`, `PUBSUB NUMPAT`), and `DemoRedisClient`.
  - `RedisPubSubPanel.razor` — channel table, pattern subscription count, manual refresh, truncation notice; empty and unsupported states.
  - `RedisOpsInsightsPanel.razor` — keyboard-accessible tab container ("Slowlog" / "Pub/Sub") with per-tab refresh delegates.
  - `RedisPage.razor` updated — `LoadSlowLogAsync` / `LoadPubSubAsync` (BL-2 compliant, CTS cancellation guard, reset on cache-switch); panel placed below `RedisPrefixMemory`.
  - 3 `DemoRedisClientTests` (unit) + 4 `RedisPubSubPanelTests` + 2 `RedisOpsInsightsPanelTests` (bUnit).

- **DI:** `RedisOpsInsightsAggregator` registered as singleton in `MauiProgram.cs`.
- **Total: Core 426/426 · App 348/348 · Build 0 errors, 0 warnings.**

## Key decisions

- **`RedisOpsInsightsAggregator` does not call Redis** — it correlates already-loaded data only, keeping hot-key scoring deterministic and testable without live connections.
- **Capability-first degradation** — all new client methods return explicit `RedisInsightCapability` states rather than throwing; panels render informational notices for `Unsupported` and `PermissionLimited`.
- **Manual refresh only** — no background polling or subscriptions; operators trigger slowlog and Pub/Sub loads explicitly per the feature plan.
- **Merged hot-key signals** — if a key has both a slowlog hit and an LFU score, the aggregator emits one merged finding rather than two, keeping the panel readable.

## Validation performed

- Unit tests: 426/426 passing (8 new in Core).
- Component tests: 348/348 passing (11 new bUnit tests).
- Build: 0 errors, 0 warnings on net10.0-windows10.0.19041.0.
- Manual (live Redis): not performed; capability-degradation paths covered by unit tests.

## Lessons learned

- `OperationCanceledException` must propagate through Redis client methods unmodified — catching `Exception` broadly and converting it to a capability state must explicitly re-throw `OperationCanceledException` first.
- Tab strip accessibility requires explicit `role="tab"`, `aria-selected`, and `aria-controls`/`aria-labelledby` pairing — browser defaults for `<button>` inside a flex row are not sufficient.
- `PUBSUB NUMSUB` returns a flat alternating key/count array, not a dictionary; parsing must read pairs, not rely on StackExchange.Redis dictionary deserialization.

## Follow-up

- Update `docs/architecture/functionalities/redis.md` to document slowlog, hot-key, and Pub/Sub surfaces — deferred; should be done before the next Redis feature.
- Manual validation on a managed Redis tier (Azure Cache for Redis) to confirm `SLOWLOG` returns `Unsupported` rather than throwing.

## Archive note

> This file is present because the feature had **no Jira ticket** (Path B). Archive location: `docs/features/archive/redis-ops-insights/`.
