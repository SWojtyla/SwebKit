# Status - observability-explainer-and-reliability

---

title: "Status - observability-explainer-and-reliability"
owner: "GitHub Copilot"
state: "Done"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-18"

---

## Quick summary

Waves 1–3 fully complete. Backend and frontend are wired end-to-end. `DeploymentComparisonPanel`, `SloStatusPanel` are implemented, `ObservabilityPage` loads anchors and SLO status on resource activation, fires anchor comparison with CTS cancellation guard. 337 App.Tests passing (9 new bUnit tests).

Jira: not linked

Current focus: validation / pre-ship review.

## Progress checklist

### Wave 1 - explanation-first overview and pivots

- [x] Define explainer summary, dependency-health, and dimension-pivot contracts
- [x] Decide which primitives belong on `IObservabilityProvider` versus a new explainer service — new `IObservabilityExplainerService` accepts provider as a parameter
- [x] Extend `IObservabilityProvider` with `GetDependencyHealthAsync` and `GetDimensionBreakdownAsync`
- [x] Implement `ObservabilityExplainerService` in `SwebKit.Core/Services/`
- [x] Implement both new provider methods in `AzureAppInsightsProvider` and `DemoObservabilityProvider`
- [x] DI registration in `MauiProgram.cs`
- [x] `DependencyHealthPanel.razor` component
- [x] `DimensionPivotPanel.razor` component
- [x] `ObservabilityExplainerSummary.razor` component
- [x] Wire into `ObservabilityPage.razor`
- [x] 5 unit tests in `SwebKit.Core.Tests` (`ObservabilityExplainerServiceTests`) — passing
- [x] 5 bUnit tests in `SwebKit.App.Tests` (`ObservabilityExplainerSummaryTests`) — passing
- [x] Define drill-through links into Logs and Incident Timeline

### Wave 2 - deployment comparison

- [x] Define `DeploymentAnchor`, `MetricDelta`, `DeploymentComparisonSummary` models in `ObservabilityModels.cs`
- [x] `GetDeploymentComparisonAsync` on `IObservabilityExplainerService` and `ObservabilityExplainerService`
- [x] Static `GetDeploymentAnchors(ReleaseRepository)` helper — picks latest `DeployedAt` per release, sorted descending
- [x] 4 unit tests (`DeploymentAnchorTests` × 3, `GetDeploymentComparisonAsync` × 4 in `ObservabilityExplainerServiceTests`)
- [x] `DeploymentComparisonPanel.razor` and wire into `ObservabilityPage`

### Wave 3 - SLO tracking

- [x] Extend `ObservabilityConfig` with `SloDefinitions` (additive, JSON-serializable)
- [x] Define `SloMetric`, `SloDefinition`, `SloStatusEntry`, `SloState`, `SloStatusSummary` in `ObservabilityModels.cs`
- [x] `GetSloStatusAsync` on `IObservabilityExplainerService` and `ObservabilityExplainerService`
- [x] 4 unit tests for `GetSloStatusAsync` in `ObservabilityExplainerServiceTests`
- [x] `SloStatusPanel.razor` and wire into `ObservabilityPage`

## Completed

- Confirmed the feature should augment the current Observability page rather than replace the Logs and guided-query escape hatches.
- Identified dependency health, custom-dimension pivots, deployment comparison, and SLO tracking as the main explanation-first gaps.
- Scoped deployment comparison toward explicit release or deployment anchors and away from guess-based change correlation.
- Wave 1 backend: `IObservabilityExplainerService` accepts provider-as-parameter (avoids AppStateService dependency); `DependencyHealthSummary`, `DimensionBreakdown`, `ObservabilityExplainerSummary` models; two new provider methods (KQL-based dep/dimension queries); `ObservabilityExplainerService` assembles anomaly signals.
- Wave 1 frontend: `DependencyHealthPanel`, `DimensionPivotPanel`, `ObservabilityExplainerSummary` components added; wired into `ObservabilityPage` above existing tabs.
- All tests: Core 407/407, App 328/328, build 0 errors 0 warnings (2026-04-18).
- Wave 2 backend: `DeploymentAnchor`, `MetricDelta`, `DeploymentComparisonSummary` models; `GetDeploymentComparisonAsync`; static `GetDeploymentAnchors`; 7 new tests (4 comparison + 3 anchor). Core 418/418, build clean (2026-04-18).
- Wave 3 backend: `SloMetric`, `SloDefinition`, `SloStatusEntry`, `SloState`, `SloStatusSummary` models; `GetSloStatusAsync`; `ObservabilityConfig.SloDefinitions`; 4 new SLO tests. Included in the 418 total above.
- Wave 2 frontend: `DeploymentComparisonPanel.razor` — anchor picker, before/after metric delta table with regression notice, loading state; wired into `ObservabilityPage` with CTS cancellation guard. 5 bUnit tests.
- Wave 3 frontend: `SloStatusPanel.razor` — no-definitions state, per-entry badge table, "All SLOs met/at risk/breached" header; wired into `ObservabilityPage` on resource activation. 4 bUnit tests.
- Final: Core 418/418, App 337/337, build 0 errors 0 warnings (2026-04-18).

## Remaining

- Manual UX validation on a real environment: confirm explanation cards are readable without opening Logs first.

## Blockers

- None.
- Jira is not linked. Informational only.

## Validation

- Test Plan: `test-plan.md`
- Validation status: All waves complete — Core 418/418, App 337/337, build 0 errors 0 warnings (2026-04-18). Manual UX validation pending.

## Notes

- Explanation-first must still leave a clear path to the underlying KQL or detail tabs.
- The feature should produce faster understanding, not a hidden analytics black box.
