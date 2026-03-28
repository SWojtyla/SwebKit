# Feature Overview - guided-kql-builder

---

title: "Feature Overview - guided-kql-builder"
owner: ""
status: "Planned"
jira: ""
created: "2026-03-28"
updated: "2026-03-28"

---

## Goal

Deliver a guided KQL builder in Observability Logs that lets non-KQL users compose useful queries through structured controls, with an advanced editor fallback for direct KQL editing.

## Value

Observability users can get actionable logs without first learning KQL syntax. This reduces first-use friction, shortens time-to-insight, and still preserves power-user workflows through an explicit raw-KQL mode.

## Scope

- In scope:
  - Guided query builder UX inside the existing Observability Logs experience in `src/SwebKit.App/Components/Observability/`.
  - Query-definition models and compile pipeline in `src/SwebKit.Core` and `src/SwebKit.Observability`.
  - Advanced KQL editor fallback with safe mode switching (guided to advanced and advanced to guided with constraints).
  - Validation and error messaging that maps builder misconfiguration and KQL execution errors to actionable UI hints.
  - Coverage in component, unit, integration, and e2e tests.

### Wave-based delivery

- Wave 1 - Guided builder foundation:
  - Define guided query model (table, time range, columns, filters, sort, row limit).
  - Implement compiler that emits deterministic KQL.
  - Add basic guided UI and run query flow from Logs tab.
- Wave 2 - Advanced fallback and persistence:
  - Add explicit mode toggle (Guided and Advanced).
  - Keep compiled query preview visible in guided mode.
  - Support editing raw KQL in advanced mode with clear handoff rules.
  - Persist mode preference and last builder state in Observability config.
- Wave 3 - Usability hardening and quality:
  - Add helpful defaults, validation hints, and low-cost query guardrails.
  - Add keyboard and accessibility polish.
  - Finalize regression coverage and UX acceptance checks.

## Non-goals

- Full bidirectional KQL parsing of arbitrary user-written KQL into builder controls.
- Multi-resource query execution in one request.
- New external query engine, service, or hosted backend outside current `IObservabilityProvider` flow.
- Replacing existing saved-query behavior; this feature augments it.

## Dependencies

- Architecture constraints:
  - UI in `src/SwebKit.App` (Observability components)
  - Abstractions and models in `src/SwebKit.Core`
  - App Insights query execution in `src/SwebKit.Observability`
- Existing Observability feature wiring in:
  - `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`
  - `src/SwebKit.App/Components/Observability/ObservabilityLogs.razor`
  - `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs`
  - `src/SwebKit.Observability/AzureAppInsightsProvider.cs`
- Pitfall files applied:
  - `docs/pitfalls/blazor-maui.md`
  - `docs/pitfalls/dotnet-csharp.md`

## Risks and mitigations

- Risk: Builder outputs invalid or expensive KQL for edge combinations.
  - Mitigation: Introduce compile-time validation with fail-fast errors and unit tests per clause combination.
- Risk: Mode switching causes confusion or accidental loss of edits.
  - Mitigation: Explicit one-way fallback behavior, unsaved-change prompts, and persistent per-resource draft state.
- Risk: Blazor component rerender behavior creates duplicate reloads and stale UI state.
  - Mitigation: Apply guard patterns from `docs/pitfalls/blazor-maui.md` (parameter guards, `InvokeAsync(StateHasChanged)`).
- Risk: Cancellation and error handling regressions in async query operations.
  - Mitigation: Preserve `OperationCanceledException` semantics per `docs/pitfalls/dotnet-csharp.md` and add integration tests.

## Related documents

- Architecture: `docs/architecture/architecture.md`
- Design flow: `docs/architecture/design.md` (Observability Resource and Query Flow)
- Navigation map: `docs/architecture/codebase-guide.md`
- Functional deep dive: `docs/architecture/functionalities/observability.md`
- Pitfalls:
  - `docs/pitfalls/blazor-maui.md`
  - `docs/pitfalls/dotnet-csharp.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules:
  - `backend.md`
  - `frontend.md`
  - `decisions.md`
