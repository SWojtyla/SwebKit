# Feature Overview - redis-ops-insights

---

title: "Feature Overview - redis-ops-insights"
owner: "GitHub Copilot"
status: "Planned"
jira: "not linked"
created: "2026-04-12"
updated: "2026-04-17"

---

## Goal

Extend the existing Redis page into an operator-grade diagnostics workspace that makes hot-key pressure, slow commands, and Pub/Sub channel activity visible without leaving the current Redis page or introducing invasive server monitoring.

## Value

Current Redis tooling in SwebKit is strong at key inspection, targeted mutation, TTL editing, prefix memory, and keyspace health for the loaded scan set. It still leaves two common operational questions unanswered:

- Are slow commands or suspected hot keys concentrated around one command, prefix, or cache region?
- Is Pub/Sub activity present on the cache, and which channels have actual subscribers right now?

Operators currently have to jump to `redis-cli`, external dashboards, or the Azure portal. That breaks the page-local context SwebKit already has: active cache entry, selected database, loaded key scan, health findings, and current key detail.

## Scope

- In scope:
- Slowlog and hot-key diagnostics built from additive server-side Redis commands plus existing `OBJECT FREQ`, `OBJECT IDLETIME`, and memory metadata.
- Read-only Pub/Sub visibility for active channels, subscriber counts, and pattern-subscription counts.
- Integration into the existing Redis page so health, prefix memory, slowlog, and Pub/Sub signals share the same selected cache and scan context.
- Additive `IRedisClient` and demo-client support plus focused tests in App and Core test projects.
- Out of scope:
- Long-running `MONITOR` streaming, packet capture, or background polling.
- Auto-remediation such as setting TTLs in bulk, killing clients, or clearing slowlog automatically.
- Full database inventory beyond the bounded loaded scan context.
- Cluster-wide topology discovery across multiple Redis nodes or shards.
- Pub/Sub message payload inspection or ad hoc subscribe/publish actions.

> Waves
>
> - Wave 1: Slowlog plus hot-key evidence with bounded read-only server diagnostics.
> - Wave 2: Pub/Sub visibility, drill-through polish, and UX hardening inside the Redis page.

## Dependencies

- Internal projects and likely touched paths:
- `src/SwebKit.App/Components/Pages/RedisPage.razor`
- `src/SwebKit.App/Components/Pages/RedisPage.razor.css`
- `src/SwebKit.App/Components/Redis/RedisKeyspaceHealthExplorer.razor`
- `src/SwebKit.App/Components/Redis/RedisPrefixMemory.razor`
- `src/SwebKit.App/Components/Redis/RedisServerInfo.razor`
- `src/SwebKit.Core/Abstractions/IRedisClient.cs`
- `src/SwebKit.Core/Models/RedisModels.cs`
- `src/SwebKit.Core/Services/RedisKeyspaceHealthAnalyzer.cs`
- `src/SwebKit.Core/Services/DemoRedisClient.cs`
- `src/SwebKit.Redis/RedisClient.cs`
- External libraries and commands:
- StackExchange.Redis server commands for `SLOWLOG`, `PUBSUB`, `INFO`, `OBJECT`, and `MEMORY`.
- Existing page-state and scan helpers in `src/SwebKit.Core/Services/RedisScanPageAccumulator.cs`.
- Pitfall files that apply:
- `docs/pitfalls/blazor-maui.md`
- `docs/pitfalls/dotnet-csharp.md`
- `docs/pitfalls/agent-workflow.md`

## Risks & mitigations

- Risk: server commands such as `SLOWLOG` or `OBJECT FREQ` are unavailable or restricted on some managed Redis tiers. - Mitigation: surface `Unsupported` or `Permission limited` states explicitly and degrade to the signals that are available.
- Risk: large keysets or slow metadata fetches make the page feel heavier than the current manual scan flow. - Mitigation: keep diagnostics manual, bounded, cancellation-aware, and scoped to the loaded key set.
- Risk: Pub/Sub or slowlog tables add too much vertical density to the current Redis page. - Mitigation: consolidate diagnostics into a tabbed "Ops Insights" surface rather than stacking more always-visible panels.
- Risk: hot-key evidence becomes noisy if it mixes heuristic and server signals without explanation. - Mitigation: annotate each finding with its source signal (`OBJECT FREQ`, idle time, slowlog frequency, or unavailable).

## Related documents

- Architecture map: `docs/architecture/architecture.md`
- Component design: `docs/architecture/design.md`
- Code navigation: `docs/architecture/codebase-guide.md`
- Functionality deep dive: `docs/architecture/functionalities/redis.md`
- Pitfalls index: `docs/pitfalls/index.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `backend.md`, `frontend.md`, `decisions.md`
