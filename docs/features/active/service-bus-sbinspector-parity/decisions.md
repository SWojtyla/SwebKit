# Decisions - service-bus-sbinspector-parity

---

title: "Decisions - service-bus-sbinspector-parity"
owner: "Unassigned"
status: "Active"

---

## Decision 001 - Capability parity over UI cloning

**Status:** Accepted

**Date:** 2026-03-28

### Context

SBInspector has deeper Service Bus operations in specific areas. SwebKit needs those capabilities, but its interface currently follows consistent cross-feature patterns and safety cues.

### Decision

Implement capability parity while preserving SwebKit interaction patterns, keyboard behavior, and production safety UX instead of replicating SBInspector layout one-to-one.

### Consequences

- Maintains consistency across SwebKit feature areas.
- Reduces user cognitive switching within SwebKit.
- Requires explicit mapping from SBInspector features into SwebKit-native UI patterns.

### Alternatives considered

- Clone SBInspector UI interactions exactly — rejected because it would create UX drift inside SwebKit.
- Keep current SwebKit scope without parity expansion — rejected because operational gaps remain high impact.

---

## Decision 002 - Severity-first wave sequencing

**Status:** Accepted

**Date:** 2026-03-28

### Context

The parity backlog spans multiple subsystems and cannot be safely delivered as one batch.

### Decision

Deliver in ordered waves: critical entity/message management, filtering, columns/density, pagination, and templates.

### Consequences

- Enables incremental rollout and validation.
- Reduces regression blast radius per release.
- Requires clear dependency tracking across backend/frontend work.

### Alternatives considered

- Single large release — rejected due review and regression risk.
- Frontend-first sequencing — rejected because contract stability is needed first for safe UI integration.

---

## Decision 003 - Destructive operations must retain production safety gates

**Status:** Accepted

**Date:** 2026-03-28

### Context

New parity scope adds destructive actions (single delete, purge all, delete filtered) that increase operational risk.

### Decision

All destructive actions must keep production-tier visual cues and explicit confirmations aligned with existing SwebKit safety behavior.

### Consequences

- Protects against accidental production impact.
- Adds deliberate friction to high-risk operations.
- Requires test coverage for production and non-production variants.

### Alternatives considered

- Fast-path destructive actions with minimal confirmation — rejected due safety risk.
- Environment-agnostic confirmation behavior — rejected because production risk is materially higher.

---

## Decision 004 - Persist user productivity preferences with backward-compatible config fields

**Status:** Accepted

**Date:** 2026-03-28

### Context

Filtering, column profiles, row density, and templates are only useful if users can reuse them between sessions.

### Decision

Persist these preferences in optional config fields and keep deserialization backward compatible for existing profile files.

### Consequences

- Improves daily productivity and parity outcomes.
- Introduces additional config schema maintenance responsibilities.
- Requires migration-safe defaults and regression tests.

### Alternatives considered

- Session-only state for all preferences — rejected because it undermines parity value.
- Separate sidecar storage per feature — rejected to avoid unnecessary persistence complexity.

---

## Decision 005 - Service Bus behavior changes require same-change-set functionality doc updates

**Status:** Accepted

**Date:** 2026-03-28

### Context

Service Bus functionality is documented in `docs/architecture/functionalities/service-bus.md`, and drift between code and docs has been a known risk.

### Decision

Any implementation that changes Service Bus behavior must update `docs/architecture/functionalities/service-bus.md` in the same change set.

### Consequences

- Keeps architecture/functionality docs trustworthy.
- Adds a mandatory documentation step to each implementation PR.

### Alternatives considered

- Defer functionality doc updates to end-of-feature only — rejected because it causes drift.
- Track changes only in feature docs — rejected because functionality docs are the durable architecture reference.

---

## Decision 006 - Prioritize functional parity and defer theming/settings and CSV export

**Status:** Accepted

**Date:** 2026-03-28

### Context

The team confirmed that existing SwebKit themes are already established and should not be a parity target in this feature. The same planning update also narrowed filtered export scope for the parity waves to JSON-first delivery.

### Decision

Keep this feature strictly focused on functional feature parity and operational capability.
Do not include settings/theming parity in this feature.
Implement filtered export parity as JSON in scope, with CSV export explicitly deferred to follow-up scope.

### Consequences

- Reduces delivery risk and shortens time to parity on highest-value operational capabilities.
- Keeps wave implementation focused on backend/frontend behavior parity instead of appearance-level parity.
- Requires explicit follow-up tracking for CSV export and any theming/settings requests.

### Alternatives considered

- Include settings/theming parity in current feature — rejected because team themes are already in place and not required for operational parity.
- Deliver JSON and CSV export together in parity waves — rejected to preserve delivery speed and reduce scope for initial parity completion.

---

## Decision 007 - Implement Wave 4 load-more as expanding peek windows

**Status:** Accepted

**Date:** 2026-03-28

### Context

Wave 4 requires load-more behavior and continuity guarantees in `MessageListView`. Existing Service Bus contracts expose count-based peek APIs but do not provide continuation tokens.

### Decision

Implement load-more by increasing the requested peek count window (`current window + page size`) and reloading the list, rather than introducing new continuation-token contracts in Wave 4.

### Consequences

- Delivers Wave 4 behavior without breaking or expanding `IServiceBusClient` contracts.
- Preserves compatibility with existing Azure/demo client implementations and test doubles.
- Keeps active filter and selection context stable because the visible dataset expands instead of replacing pagination segments.
- May request a larger cumulative message window on each load-more action; acceptable for current parity scope and mitigated by existing grid virtualization.

### Alternatives considered

- Add explicit continuation-token contracts in `IServiceBusClient` — rejected for Wave 4 due contract churn and cross-project impact.
- Keep fixed peek-only behavior without load-more — rejected because it does not meet parity scope.
