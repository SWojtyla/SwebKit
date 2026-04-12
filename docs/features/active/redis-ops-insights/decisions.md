# Decisions - redis-ops-insights

---

title: "Decisions - redis-ops-insights"
owner: "GitHub Copilot"
status: "Planned"

---

## Decision 001 - Use the loaded scan as the canonical analysis boundary

**Status:** Accepted

**Date:** 2026-04-12

### Context

The existing Redis page already has a clear performance boundary: operators scan a bounded set of matching keys, then inspect detail, health, and prefix memory within that loaded context. A second hidden full-keyspace analysis pass would be heavier, slower, and much easier to misread as authoritative cache-wide truth.

### Decision

TTL posture, hot-key analysis, and related diagnostics will use the currently loaded key scan as their primary analysis boundary and will reuse the existing coverage and confidence language when summarizing results.

### Consequences

- Keeps diagnostics honest and performance-bounded.
- Reuses the current health explorer mental model instead of inventing a second coverage system.
- Means operators must load more keys intentionally if they want broader diagnostic confidence.

### Alternatives considered

- Alternative A - Run a hidden full-keyspace diagnostic pass automatically: rejected because it breaks current performance expectations and invites false certainty.
- Alternative B - Show diagnostics with no coverage language at all: rejected because it hides the loaded-scan limitation.

---

## Decision 002 - Keep slowlog and Pub/Sub introspection manual and read-only

**Status:** Accepted

**Date:** 2026-04-12

### Context

The feature goal is deeper operational visibility, not turning SwebKit into a live Redis traffic console. Continuous `MONITOR`, background polling, or ad hoc subscribe/publish actions would raise both performance and operator-safety concerns.

### Decision

Slowlog and Pub/Sub visibility will be exposed as bounded snapshots behind explicit refresh actions. The UI will not subscribe to channels, capture payloads, publish messages, or stream monitor output.

### Consequences

- Keeps the feature operationally safe in production environments.
- Limits implementation scope to explainable, testable snapshot behavior.
- Means some transient activity can be missed between manual refreshes, which is acceptable for this planning slice.

### Alternatives considered

- Alternative A - Add live `MONITOR` streaming: rejected because it is invasive, noisy, and outside the current product safety posture.
- Alternative B - Add subscribe/publish controls with the visibility panel: rejected because it mixes diagnostics and mutation in the same feature slice.

---

## Decision 003 - Hot-key findings must preserve signal provenance

**Status:** Accepted

**Date:** 2026-04-12

### Context

Redis hot-key detection in SwebKit will combine multiple imperfect signals: LFU frequency, idle time, key size, and slowlog repetition. If the UI collapses those into one undifferentiated "hot" badge, operators cannot tell whether the signal is strong, weak, or unavailable.

### Decision

Each hot-key or slow-command finding must carry an explanation of which signals contributed to the finding and which signals were unavailable.

### Consequences

- The frontend can explain why a key or prefix is flagged.
- Unsupported commands degrade to a transparent partial-signal state instead of disappearing.
- The backend model grows slightly, but the result is far easier to trust.

### Alternatives considered

- Alternative A - One generic `Possible hot key` label with no explanation: rejected because it is too opaque.
- Alternative B - Require all signals before showing a finding: rejected because many environments will not expose every server command.
