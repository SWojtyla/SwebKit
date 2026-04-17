# Decisions - incident-investigation-workflows

---

title: "Decisions - incident-investigation-workflows"
owner: "GitHub Copilot"
status: "Planned"

---

## Decision 001 - Reuse Incident Timeline as the shared investigation target

**Status:** Accepted

**Date:** 2026-04-12

### Context

`incident-timeline-workbench` already defines the evidence model, source coverage semantics, and workload-scoped investigation flow. Creating a second investigation page for source-page drill-through would split the evidence model and likely duplicate copy, tests, and contracts.

### Decision

This feature will route Observability, Service Bus, and Pipelines drill-through actions into the existing `/incident-timeline` surface instead of creating a new investigation page.

### Consequences

- Keeps one canonical investigation model and one place to review source coverage.
- Forces launch behavior to stay compatible with the existing manual-refresh workflow.
- Makes `incident-timeline-workbench` a hard dependency for this feature.

### Alternatives considered

- Alternative A - Create a second investigation shell per source page. Rejected because it would duplicate contracts and evidence copy.
- Alternative B - Keep manual navigation only and document the workflow. Rejected because it preserves the current error-prone handoff.

---

## Decision 002 - Use an explicit investigation-seed contract instead of URL-only state

**Status:** Accepted

**Date:** 2026-04-12

### Context

The launch context can include more than a simple route parameter: time window, evidence references, selected sources, candidate workload scope, and correlation identifiers. Encoding the full payload in query-string-only navigation would be brittle and difficult to evolve.

### Decision

Introduce an explicit `IncidentInvestigationSeed` contract resolved through an app-layer launcher. Query string values may still be used for small routing hints, but the canonical launch payload should be a typed model.

### Consequences

- Reduces coupling between source pages and `IncidentTimelinePage`.
- Allows the landing banner to explain source provenance from a stable model.
- Requires stale-seed replacement and lifetime management in the app layer.

### Alternatives considered

- Alternative A - Use query-string-only navigation. Rejected because the payload is too rich and too likely to grow.

---

## Decision 003 - Register IncidentInvestigationLauncher as AddScoped, not AddSingleton

**Status:** Accepted

**Date:** 2026-04-13

### Context

`IncidentInvestigationLauncher` injects `NavigationManager` to handle the `Nav.NavigateTo` call when a seed is launched. In Blazor MAUI, `NavigationManager` is a scoped service. Registering the launcher as a singleton would cause a dependency lifetime mismatch and a runtime DI exception.

### Decision

Register `IncidentInvestigationLauncher` as `AddScoped` in `MauiProgram.cs`, not `AddSingleton`, despite being otherwise stateless.

### Consequences

- Avoids a captured-scoped-service DI error at runtime.
- The launcher is re-created per Blazor session, which is acceptable since it holds no cross-session state.

### Alternatives considered

- Alternative A - Refactor the launcher to accept `NavigationManager` as a method parameter rather than via DI injection. Viable but adds friction at call sites for no meaningful gain.
- Alternative B - Let each source page mutate `IncidentTimelinePage` state directly. Rejected because it would tightly couple unrelated pages.

---

## Decision 003 - Mapping and dependency discovery is proposal-only

**Status:** Accepted

**Date:** 2026-04-12

### Context

The feature needs to help operators move from evidence to better future investigations, but silent ownership inference would directly undermine the evidence-first goal.

### Decision

Any newly discovered workload mapping or dependency edge will be represented as a proposal with explanation text. The system may prefill a review path, but it must not persist the proposal automatically.

### Consequences

- Keeps ownership changes reviewable.
- Prevents incident workflows from mutating long-lived configuration unexpectedly.
- Requires an explicit accept path and associated tests.

### Alternatives considered

- Alternative A - Persist likely mappings automatically. Rejected because it introduces silent topology drift.
- Alternative B - Do not expose proposals at all. Rejected because it wastes reusable investigation knowledge.

---

## Decision 004 - Snapshot export is a bounded evidence bundle, not a raw data dump

**Status:** Accepted

**Date:** 2026-04-12

### Context

Operators need a portable investigation artifact, but raw payload export would quickly become oversized, noisy, and unsafe.

### Decision

Export only the normalized incident result, source coverage, selected scope, and explicitly allowed evidence fields. Redact or truncate message bodies and large payloads, and mark truncation in the export metadata.

### Consequences

- Produces an artifact that is easier to review and safer to share.
- Preserves operator trust by exposing redaction and truncation instead of hiding it.
- Requires deterministic export schema and redaction tests.

### Alternatives considered

- Alternative A - Dump full source payloads. Rejected because it creates safety and reviewability problems.
- Alternative B - Export only a screenshot-like summary. Rejected because it loses structured evidence and coverage details.

---

## Decision 005 - Watchlists and automation stay explicitly later-wave

**Status:** Accepted

**Date:** 2026-04-12

### Context

Saved watchlists and automation are attractive follow-ons, but they can quickly pull the feature toward background monitoring and implied incident detection.

### Decision

Treat watchlists and automation as later-wave scope only after launch, export, and proposal flows are proven. Any future automation must start as prefill-only and require operator confirmation.

### Consequences

- Keeps the initial implementation small, reviewable, and aligned with current product posture.
- Prevents accidental drift into hidden incident-detection behavior.
- Leaves room for later product expansion without polluting early waves.

### Alternatives considered

- Alternative A - Add watchlists and automation in the first delivery. Rejected because it broadens scope and changes product expectations too early.
- Alternative B - Ban watchlists permanently. Rejected because there is plausible later value once the evidence flow is mature.
