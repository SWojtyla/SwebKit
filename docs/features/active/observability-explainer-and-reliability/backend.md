# Backend Plan - observability-explainer-and-reliability

---

title: "Backend Plan - observability-explainer-and-reliability"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Extend the observability backend so the UI can request typed explanation summaries, dependency health, dimension pivots, deployment comparisons, and SLO evaluation on top of the current provider and logs-query model.

## Impacted areas

- Existing provider contracts and models:
- `src/SwebKit.Core/Abstractions/IObservabilityProvider.cs`
- `src/SwebKit.Core/Models/ObservabilityModels.cs`
- `src/SwebKit.Core/Domain/ObservabilityConfig.cs`
- Existing provider implementation:
- `src/SwebKit.Observability/AzureAppInsightsProvider.cs`
- `src/SwebKit.Observability/LogQueryResultProjector.cs`
- `src/SwebKit.Observability/KqlPresets.cs`
- Existing release and deployment context:
- `src/SwebKit.Core/Configuration/ReleaseRepository.cs`
- `src/SwebKit.Core/Models/ReleaseModels.cs`
- `src/SwebKit.Core/Abstractions/IDevOpsClientFactory.cs`
- `src/SwebKit.DevOps/DevOpsClientFactory.cs`
- Planned new abstractions and services:
- `src/SwebKit.Core/Abstractions/IObservabilityExplainerService.cs`
- `src/SwebKit.Core/Services/ObservabilityExplainerService.cs`

## Design

The backend should separate telemetry primitives from higher-level explanation assembly:

1. `IObservabilityProvider` stays responsible for provider-bound data retrieval such as overview metrics, exceptions, performance, availability, and new dependency or dimension queries.
2. A new `IObservabilityExplainerService` should combine those primitives with deployment anchors and SLO definitions to produce explanation-first view models.
3. Deployment comparison should anchor to an explicit `DeploymentSnapshot` or selected pipeline run, then calculate before-and-after windows from that anchor.
4. SLO evaluation should use explicit config and current telemetry; it should not infer objectives automatically from historical data.

## API / Contracts

- Likely additive provider contracts:
- `GetDependencyHealthAsync(TimeRange, ...)`
- `GetDimensionBreakdownAsync(TimeRange, dimensionKey, ...)`
- Optional helper queries for change summaries when a higher-level service cannot derive them from existing primitives cleanly.
- Likely new explainer contracts in `ObservabilityModels.cs`:
- `ObservabilityExplainerSummary`
- `DependencyHealthSummary`
- `DimensionBreakdown`
- `DeploymentComparisonSummary`
- `SloDefinition` and `SloStatusSummary`
- `DeploymentAnchor`
- Backward compatibility:
- Existing Overview, Failures, Performance, Logs, and Availability calls remain valid.
- The explainer layer should wrap current provider behavior, not replace direct access to logs or raw telemetry.

## Tasks

### Wave 1 - provider primitives and explainer service [dotnet-expert]

- [ ] Define new dependency-health and dimension-pivot models.
- [ ] Extend `IObservabilityProvider` and `AzureAppInsightsProvider` with bounded queries for those models.
- [ ] Introduce `IObservabilityExplainerService` to combine provider primitives into explanation cards.

### Wave 2 - deployment comparison [dotnet-expert]

- [ ] Define deployment-anchor models and selection rules.
- [ ] Read anchors from `ReleaseRepository` and optionally enrich them from `IDevOpsClientFactory`.
- [ ] Compute before-and-after deltas deterministically from the selected anchor.

### Wave 3 - SLO tracking [dotnet-expert]

- [ ] Extend `ObservabilityConfig` with explicit SLO definitions.
- [ ] Implement SLO evaluation and simple burn or risk summaries.
- [ ] Keep target definitions readable and bounded in config.

## Migration and runtime changes

- `ObservabilityConfig` will likely gain additive SLO definitions and maybe a small amount of explainer preference state.
- Dimension and dependency queries must respect current row or cost caps and surface truncation explicitly.
- Deployment comparison should work even when live DevOps access is unavailable by falling back to local `ReleaseRepository` data where possible.

## Validation

- Unit tests: Not started. Add explainer, deployment-window, and SLO-calculation tests in `tests/SwebKit.Core.Tests`.
- Integration tests: Not started. Extend provider and DevOps-related test suites for dependency queries, pivot normalization, and deployment anchors.
- Manual checks: verify missing deployment anchors and capped dimension results degrade explicitly.

## Notes

- `azure-sdk.md` is relevant for both App Insights resource handling and any Azure-based deployment-anchor lookups.
- Explanation services should preserve the current cancellation and stale-result rules already used by the Observability page.
