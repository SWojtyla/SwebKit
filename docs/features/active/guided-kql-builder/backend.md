# Backend Plan - guided-kql-builder

---

title: "Backend Plan - guided-kql-builder"
owner: ""
status: "Not started"

---

## Goal

Introduce a guided-query contract and compiler pipeline that generates valid KQL for the existing Observability provider flow, without breaking current raw-KQL execution paths.

## Impacted areas

- Projects and services:
  - `src/SwebKit.Core/Abstractions/`
  - `src/SwebKit.Core/Models/`
  - `src/SwebKit.Core/Domain/ObservabilityConfig.cs`
  - `src/SwebKit.Observability/AzureAppInsightsProvider.cs`
  - `src/SwebKit.Observability/KqlPresets.cs`
  - `src/SwebKit.Observability/` (new compiler and validation helpers)
- Test projects:
  - `tests/SwebKit.Core.Tests/`
  - `tests/SwebKit.Observability.Tests/`

## Design

Guided query composition remains a local app concern. Backend work introduces typed contracts and a deterministic compiler that translates a `GuidedKqlQueryDefinition` into KQL text before passing it through the existing provider execution flow described in `docs/architecture/design.md` (Observability Resource and Query Flow).

Planned backend building blocks:

- Core model types for guided query definition (table, time range, filters, projection, sort, limit).
- Compiler result model containing:
  - generated KQL text,
  - validation issues (blocking and non-blocking),
  - optional warnings (for potentially expensive clauses).
- Compiler implementation in `SwebKit.Observability` so query semantics stay close to App Insights/KQL capabilities.
- Optional config extension in `ObservabilityConfig` for storing preferred query mode and last guided draft metadata.

No new external service is introduced. Existing provider interfaces remain the runtime boundary.

## API / Contracts

- Public app-internal contracts to introduce or update:
  - Guided query definition model(s) in `SwebKit.Core/Models`.
  - Optional compiler abstraction in `SwebKit.Core/Abstractions` for testability.
  - Validation result contract with stable error codes for UI mapping.
- Backward compatibility:
  - Existing raw KQL and saved-query workflows remain supported.
  - Any new config properties must be optional and default-safe during deserialization.

## Sequencing and ownership

- Wave 1 owner: [dotnet-expert], parallel: no (foundation for frontend wiring)
- Wave 2 owner: [dotnet-expert], parallel: partial (can run alongside frontend mode UX once contracts stabilize)
- Wave 3 owner: [dotnet-expert], parallel: yes (hardening and validation)
- Review checkpoints: [manual] architecture and UX sign-off before marking review-ready.

## Tasks

### Wave 1 - Contracts and compiler foundation

- [ ] Define guided query models in `src/SwebKit.Core/Models/`.
- [ ] Define compiler abstraction and validation contract in `src/SwebKit.Core/Abstractions/`.
- [ ] Implement KQL compiler in `src/SwebKit.Observability/` with deterministic clause ordering.
- [ ] Add basic compile-time validation for unsupported operators and missing required fields.
- [ ] Add unit tests for model and compile rules in `tests/SwebKit.Core.Tests/` and `tests/SwebKit.Observability.Tests/`.

### Wave 2 - Integration and persistence

- [ ] Integrate compiled KQL path with existing query execution entry points.
- [ ] Extend `ObservabilityConfig` for mode/draft persistence using optional properties only.
- [ ] Ensure cancellation and error propagation remain distinct (do not collapse cancellation into generic errors).
- [ ] Add integration tests for compile-plus-execute path.

### Wave 3 - Hardening and telemetry

- [ ] Add warnings for high-cost query patterns (broad time windows, high row limits).
- [ ] Add structured logging around compile failures and execution handoff.
- [ ] Finalize regression tests for existing advanced KQL behavior.
- [ ] Record any implementation tradeoffs in `decisions.md`.

## Migration and runtime changes

- Migration steps:
  - No database or external schema migrations.
  - If config is extended, use optional JSON properties with defaults to avoid profile migration scripts.
- Operational runbook:
  - Rollback path is low risk: disable guided mode UI and continue raw KQL execution path.

## Validation

- Unit tests: Not started
  - Target: compiler rule coverage and serialization compatibility checks.
- Integration tests: Not started
  - Target: guided definition to provider query execution with cancellation/error semantics.
- Manual checks:
  - Verify compiled query executes in real and demo providers.
  - Verify cancellation does not surface as failure.

## Notes

- Keep `OperationCanceledException` behavior explicit per `docs/pitfalls/dotnet-csharp.md`.
- Keep query compiler independent from UI component state to prevent Blazor-specific coupling.
