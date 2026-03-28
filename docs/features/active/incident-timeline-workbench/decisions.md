# Decisions - incident-timeline-workbench

---

title: "Decisions - incident-timeline-workbench"
owner: "GitHub Copilot"
status: "Planned"

---

## Decision 001 - Use one canonical timeline model in SwebKit.Core

**Status:** Accepted

**Date:** 2026-03-28

### Context

Incident signals come from four different domains with different schemas. Without a canonical model, UI components would either contain source-specific merge logic or depend on multiple service contracts directly.

### Decision

Introduce a canonical IncidentTimelineItem and IncidentTimelinePage contract in SwebKit.Core, with a stable common shape and source-specific metadata envelope.

### Consequences

- Enables one reusable timeline UI with predictable rendering contracts.
- Keeps source-specific translation at adapter boundaries.
- Requires careful versioning of the shared model to avoid feature-coupling churn.

### Alternatives considered

- Alternative A - UI merges four native result sets directly: rejected because merge complexity and error handling move into Blazor components.
- Alternative B - Separate timeline model per source with union rendering logic: rejected due to high branching complexity and weaker testability.

---

## Decision 002 - Aggregate with best-effort partial results, not fail-fast

**Status:** Accepted

**Date:** 2026-03-28

### Context

Incident triage must remain useful when one provider is slow, unauthorized, or temporarily unavailable. A fail-fast global error would hide valid signals from healthy sources.

### Decision

IncidentTimelineService returns partial timeline data with per-source status and error details when one or more sources fail or timeout.

### Consequences

- Triage can continue with available evidence.
- UI must clearly communicate degraded coverage to prevent false confidence.
- Testing must include mixed success/failure source combinations.

### Alternatives considered

- Alternative A - Fail entire query when any source fails: rejected because it blocks triage under common transient failures.
- Alternative B - Silently drop failed sources without status: rejected because operators would not know data is incomplete.

---

## Decision 003 - Refresh behavior is cancellation-first and last-request-wins

**Status:** Accepted

**Date:** 2026-03-28

### Context

Users will frequently change range and filters during active incident response. Overlapping requests can lead to stale data rendering and unnecessary backend load.

### Decision

Each new load or refresh cancels any in-flight request via linked CancellationTokenSource. UI only applies results from the latest request version.

### Consequences

- Prevents stale-response flicker and race conditions.
- Requires explicit OperationCanceledException passthrough in all layers.
- Requires adapter and service code to be token-aware end-to-end.

### Alternatives considered

- Alternative A - Allow concurrent requests and keep first completed result: rejected because stale data can overwrite newer context.
- Alternative B - Queue all refresh requests: rejected because incident triage requires immediate latest-state feedback.

---

## Decision 004 - Apply bounded query caps and progressive rendering

**Status:** Accepted

**Date:** 2026-03-28

### Context

Large 24-hour windows can produce high event volume from AKS and DLQ signals. Rendering too many rows at once can degrade MAUI Blazor responsiveness.

### Decision

Apply per-source top-N caps and global max item limits in aggregation, then support progressive loading for additional rows through paging/cursor.

### Consequences

- Keeps first paint responsive and predictable.
- Encourages explicit exploration workflow for deep histories.
- Requires transparent UI messaging when result caps are hit.

### Alternatives considered

- Alternative A - Return all events in one response: rejected due to memory/render pressure and poor UX responsiveness.
- Alternative B - Hard truncate without paging metadata: rejected because users cannot recover omitted context.
