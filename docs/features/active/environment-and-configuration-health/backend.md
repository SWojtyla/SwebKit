# Backend Plan - environment-and-configuration-health

---

title: "Backend Plan - environment-and-configuration-health"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Create the read-only health, readiness, and environment-diff contracts needed to explain whether an operator environment is configured and ready for SwebKit's Azure-focused workflows.

## Impacted areas

- Current config and profile seams:
- `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- `src/SwebKit.Core/Domain/AppConfig.cs`
- `src/SwebKit.Core/Services/AppStateService.cs`
- Existing health/status seams:
- `src/SwebKit.Core/Services/ConnectionStateService.cs`
- `src/SwebKit.App/Platforms/Windows/WindowsCredentialStore.cs`
- Existing integration or bootstrap seams likely to support read-only probes:
- `src/SwebKit.App/Services/ServiceBusNamespaceBootstrapper.cs`
- `src/SwebKit.App/Services/AksClientBootstrapper.cs`
- `src/SwebKit.App/Services/ObservabilityProviderFactory.cs`
- `src/SwebKit.DevOps/DevOpsClientFactory.cs`
- Likely new contracts and services:
- `src/SwebKit.Core/Domain/` or `src/SwebKit.Core/Models/` for health-report and diff models
- `src/SwebKit.Core/Abstractions/` for health-report or readiness service contracts
- `src/SwebKit.Core/Services/` for report aggregation and environment-diff logic
- Tests:
- `tests/SwebKit.Core.Tests/`
- `tests/SwebKit.Azure.Tests/`
- `tests/SwebKit.Kubernetes.Tests/`
- `tests/SwebKit.DevOps.Tests/`

## Design

The backend should separate three concerns:

1. Configuration presence: is the required config shape present for a capability area?
2. Credential/reference health: does the referenced credential or Azure identity path appear available without exposing secret contents?
3. Readiness/probe outcome: can the app perform a cheap, read-only verification that the workflow is likely usable?

One aggregated service should combine those concerns into a single environment report. Environment comparison should be a sibling concern: normalize two `AppConfig` instances, compare meaningful fields, and ignore secrets or unstable runtime values.

## API / Contracts

- Likely report contracts:
- `EnvironmentHealthReport` with area-level results and an overall readiness summary.
- `EnvironmentCheckResult` with status values such as `NotConfigured`, `Configured`, `Ready`, `Warning`, `Error`, and `Skipped`.
- `CredentialReferenceStatus` that reports presence/absence and source type without exposing secret contents.
- `EnvironmentDiffResult` and `EnvironmentDiffEntry` for normalized config comparison.
- `OperatorReadinessState` for shell-friendly summary chips or checklist items.
- Backward compatibility notes:
- Existing page-local connection status should remain additive and may later consume the same report model.
- Existing profile JSON should not require manual migration just to support health reporting.

## Tasks

### Wave 1 - Canonical report and diff models [dotnet-expert] (sequential root)

- [ ] Define the health-report, readiness, and diff models in `SwebKit.Core`.
- [ ] Define normalization rules for environment comparison.
- [ ] Decide which report fields are safe for logs, UI rendering, and persistence.
- [ ] Keep secrets and raw credential values out of all report contracts.

### Wave 2 - Read-only health providers [dotnet-expert] (depends on Wave 1)

- [ ] Add or adapt read-only providers for Service Bus, AKS, Observability, Storage, DevOps, Redis, and Incident Timeline prerequisites.
- [ ] Reuse current bootstrap or factory seams where possible instead of introducing duplicate clients.
- [ ] Budget provider execution time and expose partial results cleanly.
- [ ] Ensure providers can report `Configured but not ready` without pretending success.

### Wave 3 - Environment comparison and readiness aggregation [dotnet-expert] (depends on Waves 1-2)

- [ ] Build environment diff logic across `ProfileRepository.Environments`.
- [ ] Aggregate per-area health into one operator-readiness summary.
- [ ] Expose direct Settings handoff metadata where useful.
- [ ] Keep compare/readiness logic framework-agnostic so it is testable outside the UI.

### Wave 4 - Tests and hardening [dotnet-expert] (depends on Waves 1-3)

- [ ] Add unit coverage for report normalization, diff output, and readiness-state aggregation.
- [ ] Add focused integration tests for any new probe logic against Azure/AKS/DevOps seams.
- [ ] Prove partial timeouts and auth failures degrade only the affected area.
- [ ] Record any compromises or probe limitations in `decisions.md`.

## Migration and runtime changes

- No infrastructure changes are required.
- Existing profile data should remain valid; comparison can be derived from current `Environments` structure.
- Checklist completion should be derived from config/readiness state rather than from storing a separate wizard-complete flag unless implementation proves that is necessary.

## Validation

- Unit tests: Not started.
- Integration tests: Not started.
- Manual checks:
- Verify probes remain read-only.
- Verify missing credentials and missing config can be distinguished.
- Verify environment comparison omits secrets and unstable fields.

## Notes

- Relevant pitfalls from `docs/pitfalls/azure-sdk.md`:
- AZ-1 - use the same class of operation for any Service Bus readiness probe that later workflows depend on.
- AZ-3 - dispose `AsyncPageable` enumerators correctly in cheap probe paths.
- Relevant pitfalls from `docs/pitfalls/dotnet-csharp.md`:
- CS-2 - do not swallow cancellation when health refresh is canceled or timed out.
