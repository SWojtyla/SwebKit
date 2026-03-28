# Decisions - redis-keyspace-health-explorer

---

title: "Decisions - redis-keyspace-health-explorer"
owner: ""
status: "Accepted"

---

## Decision 001 - Ship read-only health exploration before remediation actions

**Status:** Accepted

**Date:** 2026-03-28

### Context

The feature intent is early detection of risky Redis key patterns. Adding direct fix actions in the same wave would increase production risk, broaden UX safety requirements, and slow delivery.

### Decision

Wave 1 and Wave 2 deliver read-only health insights only. No automatic or direct mutative actions are included in this feature scope.

### Consequences

- Enables safer rollout with lower blast radius.
- Keeps focus on detection quality and trust in findings.
- Defers remediation UX to a follow-up feature with explicit safety workflow.

### Alternatives considered

- Alternative A: Include quick-fix TTL action in same feature.
  - Rejected: introduces mutative risk and requires additional confirmation/rollback UX.
- Alternative B: Include bulk remediation actions immediately.
  - Rejected: too large for initial scope; higher chance of operational mistakes.

---

## Decision 002 - Keep risk scoring deterministic in Core service

**Status:** Accepted

**Date:** 2026-03-28

### Context

Risk logic will evolve and needs high-confidence unit test coverage. Embedding scoring directly in page code or Redis client would make behavior harder to test and maintain.

### Decision

Implement scoring in a dedicated Core service with pure input/output contracts; Redis client remains focused on metadata retrieval.

### Consequences

- Clear separation of concerns across App/Core/Redis projects.
- Easier unit testing and threshold tuning without UI/network coupling.
- Consistent behavior across demo and live client paths.

### Alternatives considered

- Alternative A: Compute risk directly in RedisPage.
  - Rejected: UI lifecycle complexity would reduce testability and increase regression risk.
- Alternative B: Compute risk in RedisClient.
  - Rejected: mixes transport concerns with product-specific policy logic.

---

## Decision 003 - Use progressive analysis with explicit coverage reporting

**Status:** Accepted

**Date:** 2026-03-28

### Context

Redis keyspaces can be large. Full exhaustive scans for every analysis run can impact responsiveness and cause users to wait too long, while partial scans can mislead if not labeled.

### Decision

Use progressive analysis over the currently loaded keyset and include explicit coverage/confidence indicators in the report and UI.

### Consequences

- Keeps UI responsive on large datasets.
- Makes analysis limitations visible to operators.
- Supports incremental deepening by loading additional keys before rerun.

### Alternatives considered

- Alternative A: Always full keyspace scan before showing results.
  - Rejected: can be too slow and operationally expensive.
- Alternative B: Always sample silently without coverage indication.
  - Rejected: risks false confidence and poor operational decisions.
