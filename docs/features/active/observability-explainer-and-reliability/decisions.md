# Decisions - observability-explainer-and-reliability

---

title: "Decisions - observability-explainer-and-reliability"
owner: "GitHub Copilot"
status: "Planned"

---

## Decision 001 - Explanation-first augments existing tabs instead of replacing them

**Status:** Accepted

**Date:** 2026-04-12

### Context

The current Observability feature already has strong raw-detail and KQL-driven capabilities. Replacing that model would remove an important expert escape hatch.

### Decision

Add explanation-first layers on top of the existing tabs. The feature will accelerate understanding but keep direct access to Failures, Performance, Logs, and Availability.

### Consequences

- Preserves power-user workflows.
- Requires every explanation card to link to a concrete underlying detail surface.
- Keeps the Logs tab first-class.

### Alternatives considered

- Alternative A - Replace the current tabs with a summary-only view. Rejected because it would hide the underlying evidence path.
- Alternative B - Leave the page query-first only. Rejected because it leaves a major usability gap for investigation and reliability review.

---

## Decision 002 - Use a dedicated explainer service above the provider layer

**Status:** Accepted

**Date:** 2026-04-12

### Context

Provider calls should remain close to telemetry retrieval. Higher-level explanation cards need deployment anchors, SLO definitions, and multiple provider primitives composed together.

### Decision

Introduce `IObservabilityExplainerService` to assemble explanation-first outputs from provider primitives, deployment anchors, and SLO definitions instead of pushing that logic into Razor components or overloading the provider abstraction.

### Consequences

- Keeps provider interfaces focused and reusable.
- Makes explanation logic unit-testable.
- Adds one more service boundary that must be wired through DI.

### Alternatives considered

- Alternative A - Put all explanation logic in Razor components. Rejected because it would be hard to test and maintain.
- Alternative B - Put all explanation logic directly on `IObservabilityProvider`. Rejected because it would mix provider concerns with cross-feature orchestration.

---

## Decision 003 - Deployment comparison requires an explicit anchor

**Status:** Accepted

**Date:** 2026-04-12

### Context

Comparing telemetry before and after "a deployment" is only meaningful if the selected deployment is known. Guessing the latest relevant change from telemetry alone is unreliable.

### Decision

Deployment comparison will require an explicit `DeploymentSnapshot` or selected pipeline-run anchor. The UI must show which anchor is selected.

### Consequences

- Keeps before-and-after deltas auditable.
- Requires fallback behavior when no anchor is available.
- Aligns the feature with existing Pipelines and Releases data rather than vague timing guesses.

### Alternatives considered

- Alternative A - Auto-pick the nearest change in the selected range. Rejected because it is too easy to get wrong.
- Alternative B - Skip deployment comparison entirely. Rejected because it is a high-value reliability workflow.

---

## Decision 004 - Dimension pivots are explicit and capped

**Status:** Accepted

**Date:** 2026-04-12

### Context

Custom dimensions are one of the fastest ways to explain a failure spike, but unrestricted exploration can become expensive and hard to interpret.

### Decision

Dimension pivots will be explicit, top-N oriented, and capped. The UI should make truncation visible and offer drill-through rather than pretending the list is complete.

### Consequences

- Keeps cost and response size under control.
- Encourages operators to refine pivots deliberately.
- Requires cap and truncation metadata in the returned models.

### Alternatives considered

- Alternative A - Let users pivot over any dimension with uncapped results. Rejected because it is too expensive and noisy.
- Alternative B - Avoid dimension pivots entirely. Rejected because it leaves one of the most useful explanation tools on the table.

---

## Decision 005 - SLOs are explicit configuration, not inferred targets

**Status:** Accepted

**Date:** 2026-04-12

### Context

The current config already contains threshold values, but threshold colors are not the same as an SLO definition. Inferred targets would be opaque and hard to trust.

### Decision

Add explicit SLO definitions to `ObservabilityConfig` and calculate status against those configured targets.

### Consequences

- Makes reliability expectations reviewable and intentional.
- Allows current thresholds to remain useful without pretending they are full SLOs.
- Requires a clear settings and validation story for new config fields.

### Alternatives considered

- Alternative A - Infer SLO targets from current thresholds or historical baselines. Rejected because it is opaque and unstable.
- Alternative B - Keep explanation cards and skip SLO tracking. Rejected because reliability posture is part of the requested scope.
