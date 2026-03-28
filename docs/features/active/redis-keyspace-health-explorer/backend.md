# Backend Plan - redis-keyspace-health-explorer

---

title: "Backend Plan - redis-keyspace-health-explorer"
owner: ""
status: "Not started"

---

## Goal

Add a deterministic, read-only keyspace health analysis pipeline that turns Redis key metadata into actionable risk findings (no TTL, heavy prefixes, oversized values, possible hot keys) with predictable performance and clear partial-coverage semantics.

## Impacted areas

- Core contracts and models:
  - src/SwebKit.Core/Abstractions/IRedisClient.cs
  - src/SwebKit.Core/Models/RedisModels.cs
- Core analysis services:
  - src/SwebKit.Core/Services/ (new health analyzer service and thresholds)
- Redis implementation:
  - src/SwebKit.Redis/RedisClient.cs
- Redis functionality docs reference:
  - docs/architecture/functionalities/redis.md
- Test projects:
  - tests/SwebKit.Core.Tests

## Design

- Keep data collection and scoring separated:
  - Collection in Redis client and page orchestration layers.
  - Scoring in Core service (pure deterministic logic, no network side effects).
- Proposed additions in Core models:
  - RedisKeyHealthFinding (key, risk type, severity, reason, supporting metrics).
  - RedisPrefixHealthFinding (prefix, key count, memory share, risk reason).
  - RedisKeyspaceHealthReport (summary counts, coverage metadata, findings list).
  - RedisHealthThresholds/RedisHealthScanOptions (configurable defaults).
- Proposed metadata enrichment:
  - Extend RedisKeyInfo with optional frequency/idle metrics where available.
  - Keep fields nullable and optional to preserve compatibility with environments where Redis OBJECT metrics are unavailable.
- Analyzer behavior:
  - No-TTL risk: flag keys with null TTL.
  - Oversized risk: compare MemoryBytes to warning/critical thresholds.
  - Heavy-prefix risk: aggregate by top prefix (using current separator semantics) and rank by memory/key concentration.
  - Possible hot-key risk: infer from optional frequency/idle data plus size/TTL context; emit explicit "signal unavailable" state when metrics missing.

## API / Contracts

- IRedisClient contract updates (planned):
  - Option A: enrich existing GetKeyInfoAsync output with optional object metrics.
  - Option B: add separate method for optional object stats if isolation is preferred.
- Backward compatibility notes:
  - Existing callers remain valid by making new fields optional.
  - Analyzer accepts partial data and degrades gracefully when optional metrics are absent.

## Tasks

- Wave 1 foundation [dotnet-expert] (sequential)
  - [ ] Define health report and threshold models in src/SwebKit.Core/Models/RedisModels.cs
  - [ ] Add analyzer service in src/SwebKit.Core/Services (pure scoring logic)
  - [ ] Add/adjust contract surface in src/SwebKit.Core/Abstractions/IRedisClient.cs
- Wave 1 metadata retrieval [dotnet-expert] (depends on contract choice)
  - [ ] Implement optional object metric retrieval in src/SwebKit.Redis/RedisClient.cs
  - [ ] Ensure graceful fallback on unsupported Redis commands
  - [ ] Preserve cancellation semantics (rethrow OperationCanceledException)
- Wave 2 integration support [dotnet-expert + blazor-expert] (parallel after foundation)
  - [ ] Expose report-generation entry points consumable from Redis page workflow
  - [ ] Ensure report contains coverage metadata needed by frontend UX
- Wave 3 quality [dotnet-expert] (sequential)
  - [ ] Add/update unit tests in tests/SwebKit.Core.Tests
  - [ ] Add integration-oriented contract tests for fallback behavior
  - [ ] Record any additional tradeoffs in decisions.md

## Migration and runtime changes

- No schema or persistence migration expected.
- No deployment/runtime configuration changes required in this planning scope.
- Operational note: analyzer is intentionally read-only and must not issue mutative Redis commands.

## Validation

- Unit tests: Not started
- Integration tests: Not started
- Manual checks:
  - Verify analyzer output parity against known seeded datasets.
  - Verify unsupported optional metrics do not fail report generation.

## Notes

- dotnet-csharp CS-2 applies to all long-running/cancellable loops.
- Keep analyzer deterministic so behavior is testable and explainable in UI.
