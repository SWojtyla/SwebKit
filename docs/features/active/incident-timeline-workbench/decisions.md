# Decisions - incident-timeline-workbench

---

title: "Decisions - incident-timeline-workbench"
owner: "GitHub Copilot"
status: "Review"

---

## Decision 001 - Scope v1 to one workload investigation at a time

**Status:** Accepted

**Date:** 2026-04-11

### Context

The original framing risked turning the feature into a generic cross-source dashboard. That would broaden the product surface, weaken inclusion rules, and encourage users to expect cross-estate incident discovery.

### Decision

V1 is scoped to one workload investigation at a time: one profile, one cluster context, one namespace, one workload selector, and one bounded incident window.

### Consequences

- Gives the feature a concrete operational target, starting with the `prd-phonotif` pod-down workflow.
- Keeps query contracts and UI state small and easier to validate.
- Defers cross-workload and portfolio-style investigation to future scope.

### Alternatives considered

- Alternative A - Generic cross-source incident dashboard: rejected because it is too broad for a safe and achievable first cut.
- Alternative B - Namespace-wide dashboard without workload selection: rejected because it would still over-include evidence and weaken triage relevance.

---

## Decision 002 - Use one canonical evidence timeline model in SwebKit.Core

**Status:** Accepted

**Date:** 2026-04-11

### Context

Evidence comes from four different domains with different schemas. Without a canonical model, UI components would either contain source-specific merge logic or depend on multiple service contracts directly.

### Decision

Introduce a canonical IncidentTimelineItem and IncidentTimelinePage contract in SwebKit.Core, with explicit workload scope, source coverage, and link-reason metadata.

### Consequences

- Enables one reusable evidence timeline UI with predictable rendering contracts.
- Keeps source-specific translation at adapter boundaries.
- Preserves the architecture constraint that cross-source contracts live in SwebKit.Core.

### Alternatives considered

- Alternative A - UI merges four native result sets directly: rejected because merge complexity and error handling move into Blazor components.
- Alternative B - Separate timeline model per source with union rendering logic: rejected due to high branching complexity and weaker testability.

---

## Decision 003 - Use explicit link explanations and avoid causal inference

**Status:** Accepted

**Date:** 2026-04-11

### Context

Terms such as correlation, likely cause, or culprit imply more intelligence than the product can safely support in v1. The workbench should help operators inspect evidence, not infer causation on their behalf.

### Decision

Every evidence item must carry one or more explicit link reasons based on ownership or topology mapping, time-window proximity, or existing correlation IDs. Contracts and UI copy must describe these as inclusion explanations, not cause detection.

### Consequences

- Sets a safer product expectation.
- Makes it possible to explain every included item in plain language.
- Requires disciplined terminology across backend contracts, UI copy, and tests.

### Alternatives considered

- Alternative A - Keep generic correlation wording: rejected because it is ambiguous and easy to over-read as causal inference.
- Alternative B - Introduce a root-cause confidence score: rejected because the required evidence and modeling are not in scope.

---

## Decision 004 - Aggregate with best-effort partial results and explicit coverage states

**Status:** Accepted

**Date:** 2026-04-11

### Context

Incident triage must remain useful when one provider is slow, unauthorized, temporarily unavailable, or simply not mapped to the scoped workload. A fail-fast global error would hide valid evidence from healthy sources.

### Decision

IncidentTimelineService returns partial timeline data with per-source coverage states and error details when one or more sources fail, timeout, or are unmapped.

### Consequences

- Triage can continue with available evidence.
- UI must clearly communicate degraded or unmapped coverage to prevent false confidence.
- Testing must include mixed success, failure, timeout, and unmapped combinations.

### Alternatives considered

- Alternative A - Fail the entire query when any source fails: rejected because it blocks triage under common transient failures.
- Alternative B - Silently drop failed or unmapped sources: rejected because operators would not know the evidence is incomplete.

---

## Decision 005 - Keep v1 manual-refresh, bounded, and capped

**Status:** Accepted

**Date:** 2026-04-11

### Context

Auto-refresh, deep paging, and large open-ended windows would increase implementation cost and make the first release harder to reason about.

### Decision

V1 supports manual refresh, bounded windows such as 15 minutes, 1 hour, and 6 hours, and hard result caps with transparent truncation messaging.

### Consequences

- Keeps the MVP operationally useful while remaining achievable.
- Simplifies request-state management and test coverage.
- Defers auto-refresh and deeper exploration flows to later versions if proven necessary.

### Alternatives considered

- Alternative A - Auto-refresh from the start: rejected because it adds complexity before the evidence model is proven.
- Alternative B - Open-ended history with paging in v1: rejected because it broadens the product before the scoped workflow is validated.

---

## Decision 006 - Refresh behavior is cancellation-first and last-request-wins

**Status:** Accepted

**Date:** 2026-04-11

### Context

Even with manual refresh, users will frequently change workload scope or time window during active incident response. Overlapping requests can lead to stale data rendering and unnecessary backend load.

### Decision

Each new load or refresh cancels any in-flight request via linked CancellationTokenSource. UI only applies results from the latest request version.

### Consequences

- Prevents stale-response flicker and race conditions.
- Requires explicit OperationCanceledException passthrough in all layers.
- Requires adapter and service code to be token-aware end-to-end.

### Alternatives considered

- Alternative A - Allow concurrent requests and keep the first completed result: rejected because stale data can overwrite newer context.
- Alternative B - Queue refresh requests: rejected because incident triage requires the latest scope immediately.

---

## Decision 007 - Use explicit workload mapping config for non-AKS sources

**Status:** Accepted

**Date:** 2026-04-12

### Context

The existing codebase already had AKS, Observability, Service Bus, and DevOps seams, but it did not have a durable way to say which non-AKS resources belong to one workload. Falling back to resource-name similarity would over-include evidence and violate the evidence-first constraint.

### Decision

Add `AppConfig.IncidentTimeline.WorkloadMappings` as an optional environment-scoped config model. App Insights, Service Bus, and DevOps evidence is included only when the selected workload has an explicit mapping for that source.

### Consequences

- Keeps the backend additive and safe by default.
- Makes unmapped coverage visible instead of inventing ownership heuristics.
- Pushes future frontend settings work toward explicit mapping editors rather than inference-heavy discovery.

### Alternatives considered

- Alternative A - Infer ownership from shared names across resources: rejected because it would over-include unrelated evidence.
- Alternative B - Hide non-AKS sources entirely until a richer discovery system exists: rejected because explicit small mappings are enough for a safe v1 backend.

---

## Decision 008 - Keep the last loaded evidence visible while scope edits are pending

**Status:** Accepted

**Date:** 2026-04-12

### Context

V1 is manual-refresh only. If the page cleared the existing evidence timeline on every scope edit, operators would lose the currently loaded incident window before deciding whether the new scope is worth refreshing.

### Decision

The incident timeline page keeps the currently loaded evidence visible while the operator edits context, namespace, workload, time window, or source toggles. The page shows an explicit pending-refresh summary and coverage note until the user refreshes.

### Consequences

- Preserves the evidence already collected for the current investigation while the next scope is still being shaped.
- Makes the v1 manual-refresh model explicit instead of silently requerying on every edit.
- Requires a request fingerprint so the page can distinguish draft scope from the currently loaded result.

### Alternatives considered

- Alternative A - Auto-refresh on every scope change: rejected because it violates the v1 manual-refresh constraint and increases race pressure.
- Alternative B - Clear the current evidence immediately on scope edit: rejected because it hides usable evidence before the user chooses to refresh.

---

## Decision 009 - Close the mapping-discoverability gap from the incident page

**Status:** Accepted

**Date:** 2026-04-12

### Context

Live usage showed that `Unmapped` coverage was technically accurate but operationally incomplete. Operators could see that a source had no workload mapping, but the app did not provide an obvious place to fix it.

### Decision

Expose workload mapping authoring in Settings under a dedicated Incident Timeline section and let the incident page deep-link directly into that section for the current workload scope whenever selected sources are `Unmapped` or `Not configured`.

### Consequences

- Turns coverage gaps into an actionable workflow instead of a documentation-only follow-up.
- Keeps backend inclusion rules unchanged while improving operator discoverability.
- Requires Settings to support query-driven section preselection so the handoff stays focused.

### Alternatives considered

- Alternative A - Keep mapping authoring out of the app and rely on manual profile editing: rejected because it leaves a high-friction dead end in the shipped UX.
- Alternative B - Hide `Unmapped` entirely for non-AKS sources: rejected because it would trade discoverability for false confidence.
