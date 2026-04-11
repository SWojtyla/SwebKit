# Feature Overview - backend-reliability-hardening

---

title: "Feature Overview - backend-reliability-hardening"
owner: "GitHub Copilot"
status: "Review"
jira: "not linked"
created: "2026-04-11"
updated: "2026-04-11"

---

## Goal

Harden a narrow set of backend correctness and failure-handling paths so existing workflows operate on complete data and surface recoverable problems explicitly.

## Value

The landed implementation removes silent or partial backend failures without broad architectural churn:

- profile bootstrap now returns explicit `ProfileLoadResult` outcomes, keeps startup non-fatal, and blocks destructive persistence after a failed load
- real Azure DevOps callers now use immutable client snapshots created by `IDevOpsClientFactory` / `DevOpsClientFactory`
- Service Bus dead-letter complete and resubmit now process the full requested sequence set across broker batches or fail explicitly
- Redis set-member paging now uses Redis-issued `SSCAN` cursors instead of fabricated continuation state
- Application Insights row projection is now bounded at the provider boundary before truncation is finalized
- `AppEventBus` sync publish no longer logs false async-handler failures

Fixing these improves operator trust, protects persisted state, and gives later feature work a safer backend foundation without widening the architecture.

## Scope

- In scope:
- explicit profile load outcomes plus in-memory-only persistence blocking after failed profile load
- the non-fatal startup warning banner in `MainLayout`
- immutable Azure DevOps client creation and per-request PAT resolution
- exhaustive Service Bus DLQ complete/resubmit behavior across receive batches
- source-backed Redis set-member continuation parsing
- bounded App Insights row projection and truncation detection
- sync versus async `AppEventBus` dispatch cleanup
- targeted regression tests and backend/architecture doc updates
- Out of scope:
- new pages, new workflows, or UX redesigns
- broad retry-policy changes outside the existing DevOps resilience handler
- general cleanup of every repository or every backend client
- Redis keyspace scan redesign outside set-member paging correctness
- new observability features or KQL authoring features
- any schema or infrastructure migration

## Landed workstreams

- Workstream 1 - Core state safety and startup diagnostics: complete
- Workstream 2 - Integration client hardening: complete
- Workstream 3 - Regression coverage and documentation alignment: complete

## Dependencies

- Internal projects and shipped touchpoints:
- `src/SwebKit.Core`
- `src/SwebKit.DevOps`
- `src/SwebKit.Azure`
- `src/SwebKit.Redis`
- `src/SwebKit.Observability`
- `src/SwebKit.App` for minimal adoption and shell messaging
- Pitfalls that still apply to future follow-up work:
- `docs/pitfalls/azure-sdk.md`
- `docs/pitfalls/dotnet-csharp.md`
- Functionality docs updated alongside this feature:
- `docs/architecture/functionalities/service-bus.md`
- `docs/architecture/functionalities/redis.md`
- `docs/architecture/functionalities/observability.md`
- `docs/architecture/functionalities/releases.md`
- `docs/architecture/functionalities/settings-and-configuration.md`

## Risks & mitigations

- Risk: a later caller reintroduces shared mutable DevOps state.
- Mitigation: keep live-client creation behind `IDevOpsClientFactory` and avoid adding mutable `Configure()` state back onto app callers.
- Risk: operators assume profile saves still persist after a corrupted `profiles.json` load.
- Mitigation: keep the banner and blocked-save message explicit until the file is repaired.
- Risk: future Redis or Service Bus callers assume continuation tokens or receive windows are synthetic.
- Mitigation: treat cursors as source-owned tokens and keep DLQ mutation semantics exhaustive-or-fail.

## Related documents

- `docs/architecture/architecture.md`
- `docs/architecture/design.md`
- `docs/architecture/codebase-guide.md`
- `docs/pitfalls/index.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `backend.md`, `decisions.md`