# Feature Overview - backend-reliability-hardening

---

title: "Feature Overview - backend-reliability-hardening"
owner: "GitHub Copilot"
status: "Planned"
jira: "not linked"
created: "2026-04-11"
updated: "2026-04-11"

---

## Goal

Eliminate a focused set of verified backend correctness and error-handling faults across DevOps, Service Bus, Redis, Observability, and core state services so existing workflows operate on complete data and fail predictably.

## Value

Recent review findings show a small number of backend defects that are high leverage even though they are spread across multiple projects:

- Azure DevOps requests can pick up shared mutable singleton state from another configuration.
- Service Bus dead-letter completion and resubmit can silently stop after one receive batch.
- Redis set paging returns a fabricated continuation cursor that can skip or duplicate data.
- Observability query truncation applies after an overly broad projection boundary.
- Profile loading can swallow a real failure and reset state silently.
- App event publishing can log false handler failures when sync publish encounters async subscriptions.

Fixing these now improves operator trust, protects persisted state, and gives later feature work a safer backend foundation without broad architectural churn.

## Scope

- In scope:
- immutable or per-configuration DevOps client state for real Azure DevOps calls
- exhaustive DLQ completion and resubmit behavior across multiple broker receive batches
- source-backed Redis set-member paging continuation semantics
- bounded App Insights row projection before truncation is finalized
- explicit profile load failure surfacing instead of silent reset
- correct sync versus async `AppEventBus` dispatch behavior and logging
- minimal app-layer adoption work needed to consume the corrected backend shapes
- targeted regression tests and functionality-doc updates
- Out of scope:
- new pages, new workflows, or UX redesigns
- broad retry-policy changes outside the existing DevOps resilience handler
- general cleanup of every repository or every backend client
- Redis keyspace scan redesign outside set-member paging correctness
- new observability features or KQL authoring features
- any schema or infrastructure migration

## Implementation waves

- Wave 1 - Core correctness contracts and failure surfacing.
- Define the load-failure contract for profile initialization.
- Fix `AppEventBus` sync-publish semantics.
- Preserve non-fatal startup while making failures visible.
- Wave 2 - Integration client hardening.
- Remove shared mutable DevOps configuration.
- Fix DLQ multi-batch mutation behavior.
- Fix Redis set-member cursor behavior.
- Bound Observability row projection at the provider boundary.
- Wave 3 - Adoption, regression safety, and docs.
- Update minimal app callers and DI registrations.
- Extend regression coverage in the affected test projects.
- Update functionality docs for changed behavior.

## Dependencies

- Internal projects and likely touchpoints:
- `src/SwebKit.Core`
- `src/SwebKit.DevOps`
- `src/SwebKit.Azure`
- `src/SwebKit.Redis`
- `src/SwebKit.Observability`
- `src/SwebKit.App` for DI and caller adoption only
- Pitfalls that apply:
- `docs/pitfalls/azure-sdk.md`
- `docs/pitfalls/dotnet-csharp.md`
- Functionality docs expected to change during implementation:
- `docs/architecture/functionalities/service-bus.md`
- `docs/architecture/functionalities/redis.md`
- `docs/architecture/functionalities/observability.md`
- `docs/architecture/functionalities/releases.md`
- `docs/architecture/functionalities/settings-and-configuration.md`

## Risks & mitigations

- Risk: DevOps lifetime changes regress existing Pipelines, Dashboard, or Tag Manager flows.
- Mitigation: keep the `IDevOpsClient` method surface stable, move configuration isolation behind a factory or session boundary, and add app regression checks for the affected consumers.
- Risk: DLQ fixes still leave partial mutation behavior under concurrency.
- Mitigation: require the operation to either process the full requested sequence set or fail explicitly with missing sequence information.
- Risk: Redis cursor correctness removes the illusion of stable offset ordering.
- Mitigation: treat the cursor as opaque and validate no duplicate or skipped members instead of relying on a fabricated offset.
- Risk: surfacing profile load failures changes startup expectations.
- Mitigation: keep initialization non-fatal, expose diagnostics through `AppStateService`, and avoid destructive auto-save after failed load.
- Risk: Observability cap changes alter logs-tab messaging or assumptions.
- Mitigation: keep the existing provider contract, add direct regression coverage for truncation, and verify the manual logs flow.

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