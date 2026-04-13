# Decisions - environment-and-configuration-health

---

title: "Decisions - environment-and-configuration-health"
owner: "GitHub Copilot"
status: "Planned"

---

## Decision 001 - Health and readiness checks must be read-only and time-budgeted

**Status:** Accepted

**Date:** 2026-04-12

### Context

The feature exists to improve operator trust before they use the app. If the readiness path performs mutating or expensive external operations, it becomes a new source of risk.

### Decision

All health and readiness checks introduced by this feature must be read-only, cheap, and explicitly budgeted for timeouts or partial failures.

### Consequences

- The app can surface trustworthy readiness without side effects.
- Some checks may remain probabilistic rather than proving every workflow path fully end to end.

### Alternatives considered

- Alternative A - run richer live operations for "real" health: rejected because it increases risk and cost for a shell-level readiness feature.

---

## Decision 002 - Report credential health without exposing secret values

**Status:** Accepted

**Date:** 2026-04-12

### Context

Operators need to know whether required credentials exist, but SwebKit must not leak secret contents in UI, logs, or comparison views.

### Decision

Readiness reporting may describe credential references, source type, and presence or absence, but it must never render or compare secret values.

### Consequences

- The UI can still explain why a capability is not ready.
- Comparison and health-report logic need a clear whitelist of safe fields.

### Alternatives considered

- Alternative A - show secret excerpts to improve troubleshooting: rejected because it is unsafe and unnecessary.

---

## Decision 003 - Readiness summaries should normalize config, not live runtime state

**Status:** Accepted

**Date:** 2026-04-12

### Context

Operators need a stable explanation of what is configured and what is missing. Mixing runtime probe state with raw config output would make the result noisy and unstable.

### Decision

Readiness summaries and configuration-gap output should operate on normalized config plus safe credential-reference metadata, not on volatile runtime status.

### Consequences

- Summaries stay stable and explainable.
- Runtime readiness remains a separate health-report concern.

### Alternatives considered

- Alternative A - mix live readiness into configuration summaries: rejected because it makes the output too noisy and time-variant.

---

## Decision 004 - Derive checklist progress from actual state instead of storing a separate wizard state

**Status:** Accepted

**Date:** 2026-04-12

### Context

Checklist experiences often introduce their own completion flags, which then drift from actual config and health state.

### Decision

The first-run or not-ready checklist should be derived from current config and readiness results rather than from a separate wizard-progress model, unless implementation proves a narrow exception is required.

### Consequences

- The checklist remains truthful even after manual config edits.
- The UI must compute readiness clearly instead of relying on simple completion toggles.

### Alternatives considered

- Alternative A - store separate checklist completion flags: rejected because it creates a second source of truth.

---

## Decision 005 - Use `Configured` when local prerequisites exist but live readiness is not yet proven

**Status:** Accepted

**Date:** 2026-04-13

### Context

Some capability areas have enough local configuration to be trustworthy in the shell, but a true `Ready` state would still require a live read-only probe or an external identity path the app has not verified yet.

### Decision

The readiness report uses `Configured` for areas such as AKS, Observability, and AAD-backed Storage when local prerequisites are present but the feature has not yet run a live read-only verification. `Ready` is reserved for cases where the current slice can prove the local prerequisites are usable without leaking secrets.

### Consequences

- The UI can distinguish "you configured this" from "the app can trust this right now."
- Later live-probe work can upgrade `Configured` to `Ready` without changing the shell-level contract.

### Alternatives considered

- Alternative A - collapse `Configured` into `Ready`: rejected because it would overstate trust for flows that still depend on unverified runtime identity or connectivity.
