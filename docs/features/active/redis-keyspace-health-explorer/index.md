# Feature Overview - redis-keyspace-health-explorer

---

title: "Feature Overview - redis-keyspace-health-explorer"
owner: ""
status: "Planned"
jira: ""
created: "2026-03-28"
updated: "2026-03-28"

---

## Goal

Provide a read-only Redis keyspace health explorer that highlights risky keys early (no TTL, heavy prefixes, oversized values, possible hot keys) so operators can prevent cache outages and performance regressions before they impact users.

## Value

Redis outages in this product are usually preceded by unhealthy key patterns that are hard to spot in the current tree/detail workflow. This feature adds fast risk visibility for on-call and day-to-day operations without introducing mutative behavior, so teams can identify and prioritize remediation before memory pressure, eviction storms, or latency spikes occur.

## Scope

- In scope:
  - Wave 1 - Health data and scoring foundation:
    - Define health report models in Core for key-level and prefix-level findings.
    - Extend Redis metadata collection to support no-TTL, size, and best-effort hot-key signals.
    - Implement deterministic analyzer service in Core with configurable thresholds.
  - Wave 2 - Redis Health Explorer UI:
    - Add health panel in Redis page with summary cards, severity filters, and findings table.
    - Provide drill-through from finding to existing key detail panel.
    - Surface scan coverage and confidence (loaded keys vs estimated keyspace) to avoid false certainty.
  - Wave 3 - Validation and rollout hardening:
    - Add unit/component/integration/e2e coverage for high-risk flows.
    - Add cancellation/rescan hardening for large keyspaces and repeated scans.
    - Document operational caveats in feature docs before implementation handoff.
- Out of scope:
  - Any automatic fix action (auto TTL set/remove, auto delete, auto rename).
  - Cluster-wide cross-node aggregation beyond the selected cache/database context.
  - Replacing existing Redis tree/detail pages.
  - Background scheduled scanning or long-term historical trend storage.

## Dependencies

- UI surface:
  - src/SwebKit.App/Components/Pages/RedisPage.razor
  - src/SwebKit.App/Components/Redis/
- Core abstractions and models:
  - src/SwebKit.Core/Abstractions/IRedisClient.cs
  - src/SwebKit.Core/Models/RedisModels.cs
  - src/SwebKit.Core/Services/
- Redis implementation:
  - src/SwebKit.Redis/RedisClient.cs
- Existing Redis functionality reference:
  - docs/architecture/functionalities/redis.md
- Pitfall files that apply:
  - docs/pitfalls/blazor-maui.md
  - docs/pitfalls/dotnet-csharp.md

## Risks & mitigations

- Risk: Large-keyspace scans increase Redis and UI load.
  - Mitigation: Keep scan operations paged and cancellable, cap per-wave analysis budget, and clearly label partial coverage.
- Risk: Hot-key signals are not universally available across Redis configurations.
  - Mitigation: Use nullable optional metrics (for example OBJECT FREQ and IDLETIME) with fallback heuristics and "signal unavailable" states.
- Risk: Users may treat risk output as exact truth.
  - Mitigation: Show confidence/coverage indicators and explain heuristic boundaries in-panel.
- Risk: Async UI refresh races can cause stale rendering.
  - Mitigation: Follow blazor-maui pitfalls for guard-before-await and InvokeAsync(StateHasChanged).
- Risk: Cancellation gets swallowed and long operations continue.
  - Mitigation: Follow dotnet-csharp CS-2 pattern and rethrow OperationCanceledException explicitly in catch blocks.

## Related documents

- Architecture:
  - docs/architecture/architecture.md
  - docs/architecture/design.md
  - docs/architecture/codebase-guide.md
  - docs/architecture/functionalities/redis.md
- Pitfalls:
  - docs/pitfalls/blazor-maui.md
  - docs/pitfalls/dotnet-csharp.md

## Quick links

- Jira: not linked
- Status: status.md
- Tests: test-plan.md
- Implementation modules: backend.md, frontend.md, decisions.md
