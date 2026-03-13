# Backend Plan - Redis Follow-up

---

title: "Backend Plan - Redis Follow-up"
owner: ""
status: "Done"

---

## Goal

Add backend/domain support for multiple Redis caches per environment and provide data contracts/services for namespace grouping and prefix memory analysis.

## Impacted areas

- Projects / services: `src/SwebKit.Core`, `src/SwebKit.Redis`
- Configuration/domain: Redis config model in project environment state
- Demo and production Redis clients

## Design

- Evolve Redis configuration from single `RedisConfig` to a backward-compatible collection model (e.g., `RedisCaches` with selected key/id).
- Preserve ability to read old config snapshots and map them into the new model.
- Add namespace aggregation helper that groups scanned keys by configurable separator.
- Add prefix memory aggregation helper that computes per-prefix totals from scanned key metadata/memory usage.

## API / Contracts

- Update domain contract for Redis cache selection and editable display name.
- Add DTO(s) for namespace tree nodes and prefix memory buckets.
- Define behavior for custom separator (validation, default, fallback).

## Tasks

- [ ] Define/update contracts
- [ ] Implement domain logic
- [ ] Implement infrastructure/persistence changes
- [ ] Add/update error handling
- [ ] Add/update logging & telemetry
- [ ] Add/update unit & integration tests

## Migration and runtime changes

- Add compatibility logic for existing persisted `RedisConfig`.
- Ensure existing environments continue to work with zero manual migration.
- Keep persistence format stable and version-aware where needed.

## Validation

- Unit tests: Not started
- Integration tests: Not started
- Manual checks: verify old config load + new multi-cache persistence roundtrip

## Notes

- Keep helper logic deterministic for testability (grouping and memory rollups).
- Guard memory analysis to avoid expensive full keyspace traversal by default.
