# Status - redis-ops-insights

---

title: "Status - redis-ops-insights"
owner: "GitHub Copilot"
state: "In Progress"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-18"

---

## Quick summary

Wave 1 (slowlog + hot-key) and Wave 2 (Pub/Sub) backend and UI implementation is complete. Redis functionality docs update remains.

Jira: not linked

Current focus: Update `docs/architecture/functionalities/redis.md` with final UI behavior.

## Progress checklist

### Planning

- [x] Scope aligned with the existing Redis page, keyspace health explorer, and current `IRedisClient` seam.
- [x] Safety guardrails defined for server-side diagnostics and manual refresh behavior.
- [x] Likely impacted source files and tests documented.

### Wave 1 - Slowlog and hot-key evidence

- [x] Extend `IRedisClient` and `RedisClient` with bounded slowlog access and command-summary models.
- [x] Add multi-signal hot-key aggregation with explicit explanation text (`RedisOpsInsightsAggregator`).
- [x] Add UI summaries and drill-through into the selected key (`RedisSlowLogPanel`, `RedisOpsInsightsPanel` wired to `SelectKeyAsync`).

### Wave 2 - Pub/Sub visibility and polish

- [x] Add Pub/Sub snapshot contracts and client methods.
- [x] Add channel and subscriber UI with manual refresh (`RedisPubSubPanel`).
- [ ] Update `docs/architecture/functionalities/redis.md` with final UI behavior.

## Completed

- Framed the feature as an extension of the current Redis page instead of a separate diagnostics route.
- Kept all new diagnostics read-only and manual-refresh only.
- Scoped diagnostics to slowlog, hot-key evidence, and Pub/Sub visibility; TTL forecasting is not in scope.
- **Wave 1 backend**: `RedisSlowLogSummary`, `RedisHotKeySignal`, `RedisHotKeySummary`, `RedisInsightCapability` models added to `RedisModels.cs`. `GetSlowLogAsync` added to `IRedisClient`, `RedisClient`, and `DemoRedisClient`. `RedisOpsInsightsAggregator` service created and registered as singleton.
- **Wave 2 backend**: `RedisPubSubChannelInfo`, `RedisPubSubSnapshot` models added. `GetPubSubSnapshotAsync` added to `IRedisClient`, `RedisClient`, and `DemoRedisClient`.
- **Wave 1+2 UI**: `RedisSlowLogPanel`, `RedisPubSubPanel`, and `RedisOpsInsightsPanel` (tab container) created. `RedisPage.razor` updated with `_slowLog`, `_hotKeys`, `_pubSub` fields, `LoadSlowLogAsync`/`LoadPubSubAsync` methods (BL-2, BL-3 compliant), cache-switch reset, `_insightsCts` disposal.
- **Tests**: 11 new bUnit tests across 3 test classes. All pass, 0 build errors, 0 warnings.

## Remaining

- Update `docs/architecture/functionalities/redis.md` once UI is reviewed.

## Blockers

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- New diagnostics must never auto-run background polling loops.
- Coverage and confidence labels must remain visible anywhere the UI summarizes TTL, hot-key, or Pub/Sub findings.
- The existing key-detail mutation flows remain separate from the new diagnostics work; this feature deepens visibility first.
