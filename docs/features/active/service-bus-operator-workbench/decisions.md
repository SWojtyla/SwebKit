# Decisions - service-bus-operator-workbench

---

title: "Decisions - service-bus-operator-workbench"
owner: "GitHub Copilot"
status: "Planned"

---

## Decision 001 - Keep the workbench on the existing `/service-bus` route

**Status:** Accepted

**Date:** 2026-04-12

### Context

The current Service Bus page already owns namespace connection, entity browse, message detail, DLQ handling, scheduled messages, and composer workflows. Splitting new operator features into a second route would fragment the operator model.

### Decision

Deepen the existing `/service-bus` route and component tree instead of creating a second operator page.

### Consequences

- Preserves one mental model for Service Bus operations.
- Requires careful layout work so the page does not become overcrowded.
- Encourages reuse of current selection state, confirmations, and persisted preferences.

### Alternatives considered

- Alternative A - Build a second advanced route. Rejected because it would duplicate entity and selection logic.
- Alternative B - Keep current page unchanged and document extra steps. Rejected because it leaves the operator pain unaddressed.

---

## Decision 002 - Session inspection is on-demand, not background-driven

**Status:** Accepted

**Date:** 2026-04-12

### Context

Sessionized entities are important for triage, but background session receivers would add hidden broker activity, lock concerns, and more fragile desktop runtime behavior.

### Decision

Implement bounded, on-demand session inspection only. The page may query session summaries when the operator asks for them, but it will not maintain background listeners.

### Consequences

- Keeps the app operationally transparent.
- Avoids hidden session-lock side effects.
- Requires the UI to explain the limits of the currently loaded session view.

### Alternatives considered

- Alternative A - Continuously poll or receive sessions in the background. Rejected because it changes the runtime posture too much.
- Alternative B - Ignore session workflows entirely. Rejected because it leaves a major gap for sessionized workloads.

---

## Decision 003 - Trace pivots are based on explicit identifiers only

**Status:** Accepted

**Date:** 2026-04-12

### Context

Operators need faster pivots from a message into related evidence, but fuzzy joins based on naming or partial payload inspection would quickly overstate the relationship.

### Decision

Build trace pivots only from explicit identifiers already present on the message or its application properties, such as `MessageId`, `CorrelationId`, `SessionId`, `operation_Id`, or `traceparent`-derived fields.

### Consequences

- Keeps pivots explainable and auditable.
- Limits the number of automatic downstream links.
- Requires the UI to explain which key is being used.

### Alternatives considered

- Alternative A - Guess relationships from payload text. Rejected because it is brittle and misleading.
- Alternative B - Avoid any downstream pivots. Rejected because it leaves cross-tool investigation friction unchanged.

---

## Decision 004 - Batch replay and send remain preview-first

**Status:** Accepted

**Date:** 2026-04-12

### Context

The feature needs higher-throughput operator actions, but the cost of a mistaken replay or send in production is high.

### Decision

All new batch replay and batch send flows must go through an explicit preview that summarizes target, count, remap rules, and environment before execution.

### Consequences

- Keeps the app aligned with its existing production-safe confirmation posture.
- Makes partial-success reporting a first-class part of the backend and UI design.
- Adds dialog complexity that must be covered by tests.

### Alternatives considered

- Alternative A - Allow one-click replay for selected batches. Rejected because it is too easy to misuse.
- Alternative B - Keep only one-message actions. Rejected because it does not solve the operator throughput gap.

---

## Decision 005 - Reuse existing `RemapRules` and mutation paths

**Status:** Accepted

**Date:** 2026-04-12

### Context

The current codebase already contains `RemapRules`, `SendBatchAsync`, and dead-letter replay or completion operations. A separate batch-mutation subsystem would duplicate behavior and increase validation burden.

### Decision

Batch send and replay planning will build around the existing mutation contracts and remap model, extending them only where the current types cannot express preview or execution-summary needs.

### Consequences

- Minimizes backend churn.
- Keeps the operator model consistent with current compose and replay flows.
- Requires careful additive design for preview contracts rather than reworking the existing APIs wholesale.

### Alternatives considered

- Alternative A - Create a new bulk-operations engine. Rejected because it would duplicate current capabilities.
- Alternative B - Avoid preview contracts entirely and let the UI infer the preview locally. Rejected because backend validation belongs in one place.
