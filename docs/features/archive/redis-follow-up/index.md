# Feature Overview - Redis Follow-up

---

title: "Feature Overview - Redis Follow-up"
owner: ""
status: "Done"
created: "2026-03-12"
updated: "2026-03-13"

---

## Goal

Improve the Redis UX for multi-cache workflows and day-to-day keyspace navigation, while removing low-value UI elements.

## Value

This follow-up removes friction in core Redis workflows: selecting among multiple caches, understanding key organization by namespace, and making destructive actions clearer. It also drops unused UI to keep the surface focused.

## Scope

### In scope

- Key namespace grouping with configurable separator and tree view presentation.
- Prefix-level memory analysis view (per-prefix memory distribution).
- Remove Server Info action from the Redis page toolbar.
- Rename `Flush DB` action to `Purge All` in UI and confirmations.
- Add pattern examples/help near the SCAN pattern field.
- Display selected cache name (editable by user) instead of static `Redis` label.
- Support multiple Redis caches per environment with dropdown selection.

### Out of scope

- Pub/Sub monitor.
- Slow log viewer.
- Import/export.
- Cluster topology.
- Keyspace notifications.
- Sentinel management.

## Dependencies

- Existing Redis implementation in `src/SwebKit.App/Components/Pages/RedisPage.razor` and `src/SwebKit.App/Components/Pages/RedisConfigForm.razor`.
- Core Redis domain model updates for multi-cache configuration.
- Existing `StackExchange.Redis` client in `src/SwebKit.Redis/`.

## Risks & mitigations

- Risk: Namespace grouping on large keyspaces can cause expensive client-side processing. — Mitigation: compute groups incrementally from scanned pages and keep aggregation bounded.
- Risk: Memory analysis may be expensive if implemented as full scan. — Mitigation: make sampling strategy explicit and expose coverage indicators in UI.
- Risk: Multi-cache configuration may break existing single-cache settings. — Mitigation: add backward-compatible model migration path and defaults.

## Related documents

- Archived Redis feature: `docs/features/archive/redis/`
- Architecture: `docs/architecture/architecture.md`
- Pitfalls: `docs/pitfalls/blazor-maui.md`

## Quick links

- Status: `status.md`
- Backend plan: `backend.md`
- Frontend plan: `frontend.md`
- Decisions: `decisions.md`
- Test plan: `test-plan.md`
