# Archive Summary - guided-kql-builder

---

title: "Archive Summary - guided-kql-builder"
owner: ""
jira: ""
completed_date: "2026-03-28"
pr: ""
commit: ""

---

## Goal

Deliver a guided KQL builder in Observability Logs so non-KQL users can compose useful queries through structured controls, while preserving an advanced raw-KQL path for expert users.

## Delivered

- Added guided query-definition contracts and deterministic KQL compilation support across Core and Observability layers.
- Added guided Logs UX with table, time range, filters, sort, limit, and compiled query preview.
- Added explicit Guided and Advanced mode switching with guided-to-advanced handoff and persisted mode or draft behavior.
- Added validation and run guardrails that block execution on compile errors while keeping warning-only execution available.
- Added focused automated coverage for guided Logs compile, mode switching, and run behavior.

## Key decisions

- Use one-way guided-to-KQL compilation instead of full reverse parsing to keep behavior predictable and maintainable.
- Keep shared contracts in Core and provider-specific compilation in Observability to preserve project boundaries.
- Preserve existing raw KQL execution as a compatibility anchor for current users.
- Block guided Run on compile errors, but keep warnings non-blocking to avoid over-restricting exploratory queries.

## Validation performed

- Unit and component checks: focused guided Logs coverage passed (`dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj --filter "FullyQualifiedName~ObservabilityLogs|FullyQualifiedName~GuidedKql"`).
- Build: solution build passed (`dotnet build SwebKit.slnx`).
- E2E: guided journeys were added but local run is environment-blocked (`REGDB_E_CLASSNOTREG` and CDP startup timeout).

## Lessons learned

- Inline field-level validation and explicit run guardrails reduce failed query attempts and improve operator confidence.
- Keeping advanced mode intact minimizes regression risk when introducing guided experiences.
- MAUI E2E prerequisites should be validated early to avoid late-stage validation blockers.

## Follow-up

- Re-run E2E guided journeys in an environment with required MAUI runtime and CDP startup support - owner: feature maintainer.
- Consider future reverse-parse support only if product value justifies parser complexity - owner: product backlog.

## Archive note

> This file is present when the feature had **no Jira ticket** (Path B). If a Jira ticket existed, the feature folder was deleted after merge and the ticket is the durable record. Archive location: `docs/features/archive/<feature-name>/`.