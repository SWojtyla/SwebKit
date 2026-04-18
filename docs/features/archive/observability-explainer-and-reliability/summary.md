# Archive Summary - observability-explainer-and-reliability

---

title: "Archive Summary - observability-explainer-and-reliability"
owner: "GitHub Copilot"
jira: "not linked"
completed_date: "2026-04-18"
pr: ""
commit: ""

---

## Goal

Move the Observability experience from query-first to explanation-first by adding dependency health, custom-dimension pivots, deployment before-and-after comparison, and explicit SLO tracking while preserving direct drill-through into logs and Incident Timeline.

## Delivered

- **Wave 1 — Explanation-first overview and pivots:**
  - `DependencyHealthSummary` and `DimensionBreakdown` models + two new `IObservabilityProvider` methods (`GetDependencyHealthAsync`, `GetDimensionBreakdownAsync`) — KQL-based, capped at configurable row limits.
  - `IObservabilityExplainerService` (provider accepted as parameter, no AppStateService dependency) + `ObservabilityExplainerService` assembles anomaly signals from dep/pivot primitives.
  - `DependencyHealthPanel.razor`, `DimensionPivotPanel.razor`, `ObservabilityExplainerSummary.razor` — wired into `ObservabilityPage` above existing tabs.
  - DI registered as singleton in `MauiProgram.cs`.
  - 5 unit tests (`ObservabilityExplainerServiceTests`) + 5 bUnit tests (`ObservabilityExplainerSummaryTests`).

- **Wave 2 — Deployment before-and-after comparison:**
  - `DeploymentAnchor`, `MetricDelta`, `DeploymentComparisonSummary` models.
  - `GetDeploymentComparisonAsync` — computes before/after `OverviewMetrics` windows around anchor; detects regression when failure rate rises >10 percentage points or P95 rises >20%.
  - Static `GetDeploymentAnchors(ReleaseRepository)` — picks latest `DeployedAt` per release, sorted descending.
  - `DeploymentComparisonPanel.razor` — anchor picker, metric delta table with ▲/▼ arrows + text labels, "Regression detected" / "No regression" notices, CTS cancellation guard in page.
  - 4 comparison unit tests + 3 anchor tests + 5 bUnit tests.

- **Wave 3 — SLO tracking:**
  - `SloDefinition` (JSON-serializable, config-driven), `SloStatusEntry`, `SloState`, `SloStatusSummary` models.
  - `ObservabilityConfig.SloDefinitions` — additive list property, safe for existing configs.
  - `GetSloStatusAsync` — evaluates `FailureRate`, `P95ResponseTimeMs`, and `AvailabilityPct` against configured targets; supports warn bands.
  - `SloStatusPanel.razor` — no-definitions state, per-entry badge table, header badge ("All SLOs met" / "SLO at risk" / "SLO breached") — all state coloring paired with text labels.
  - 4 SLO unit tests + 4 bUnit tests.

- **Total: Core 418/418 · App 337/337 · Build 0 errors, 0 warnings.**

## Key decisions

- **Provider-as-parameter on `IObservabilityExplainerService`** — avoids injecting `AppStateService` into a Core service; the page resolves the provider and passes it in, keeping the service independently testable.
- **Static `GetDeploymentAnchors` helper** — anchors are derived from already-loaded `ReleaseRepository` state; no async call needed, no new DI dependency.
- **`SloDefinition` uses `init` setters** — required for JSON deserialization via `System.Text.Json` without a custom converter; constructor-param records would break config round-trips.
- **Regression defined as >10pp failure-rate delta or >20% P95 delta** — explicit numeric thresholds, visible in code and docs, avoiding magic number drift.

## Validation performed

- Unit tests: Core 418/418 (including 11 new Wave 1 + 8 new Wave 2 + 4 new Wave 3 tests).
- Component tests: App 337/337 (including 14 new bUnit tests across Wave 1–3 panels).
- Build: 0 errors, 0 warnings on net10.0-windows10.0.19041.0.
- Manual: not performed; feature is explanation-only, no destructive paths.

## Lessons learned

- Passing `IObservabilityProvider` as a method parameter rather than constructor-injecting it via a factory keeps explainer logic testable with a plain stub and no DI ceremony.
- `SloDefinition` must use public `init` properties, not primary-constructor parameters, for `System.Text.Json` round-trip compatibility without a custom converter.
- Anchor comparison needs a `CancellationTokenSource` per selection — if the operator picks a different anchor before the first comparison returns, the stale result overwrites the current one silently.

## Follow-up

- Manual UX validation on a real App Insights resource — confirm explanation cards are readable and the dependency table populated without opening Logs first.
- SLO Settings UI — surfacing `SloDefinitions` config editor in the Settings page is deferred; currently operators must edit the profile JSON directly.

## Archive note

> This file is present because the feature had **no Jira ticket** (Path B). Archive location: `docs/features/archive/observability-explainer-and-reliability/`.
