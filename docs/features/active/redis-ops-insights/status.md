# Status - redis-ops-insights

---

title: "Status - redis-ops-insights"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-12"

---

## Quick summary

Planning is complete for a three-wave Redis diagnostics expansion. The next useful implementation step is Wave 1: additive TTL posture contracts and a bounded TTL forecast surface on the current Redis page.

Jira: not linked

Current focus: turn the current scan and health coverage model into a reusable base for TTL distribution and forecast reporting.

## Progress checklist

### Planning

- [x] Scope aligned with the existing Redis page, keyspace health explorer, and current `IRedisClient` seam.
- [x] Safety guardrails defined for server-side diagnostics and manual refresh behavior.
- [x] Likely impacted source files and tests documented.

### Wave 1 - TTL posture and forecast

- [ ] Add TTL insight contracts and analyzer services in `SwebKit.Core`.
- [ ] Extend `RedisPage` with TTL distribution and forecast UI on the existing scan context.
- [ ] Add unit and component coverage for bucket math, coverage messaging, and drill-through.

### Wave 2 - Slowlog and hot-key evidence

- [ ] Extend `IRedisClient` and `RedisClient` with bounded slowlog access and command-summary models.
- [ ] Add multi-signal hot-key aggregation with explicit explanation text.
- [ ] Add UI summaries and drill-through into the selected key or prefix.

### Wave 3 - Pub/Sub visibility and polish

- [ ] Add Pub/Sub snapshot contracts and client methods.
- [ ] Add channel and subscriber UI with manual refresh and filter carry-over from the current key context.
- [ ] Run focused App and Core test passes and update Redis functionality docs with the final behavior.

## Completed

- Framed the feature as an extension of the current Redis page instead of a separate diagnostics route.
- Kept all new diagnostics read-only and manual-refresh only.
- Chosen a bounded analysis model that reuses the current scan and health coverage language rather than pretending to inspect the full keyspace.

## Remaining

- Implement Wave 1 TTL posture and forecast reporting.
- Implement Wave 2 slowlog and hot-key evidence surfaces.
- Implement Wave 3 Pub/Sub visibility and final UX consolidation.
- Validate focused Core/App test slices and update `docs/architecture/functionalities/redis.md` during implementation.

## Blockers

- Jira ticket is not linked (informational).
- Some Redis tiers may not expose the same server commands (`SLOWLOG`, `OBJECT FREQ`, Pub/Sub introspection). The plan already assumes capability-based degradation rather than a hard dependency.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- New diagnostics must never auto-run background polling loops.
- Coverage and confidence labels must remain visible anywhere the UI summarizes TTL, hot-key, or Pub/Sub findings.
- The existing key-detail mutation flows remain separate from the new diagnostics work; this feature deepens visibility first.
